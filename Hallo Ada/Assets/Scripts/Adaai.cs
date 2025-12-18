using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class Adaai : MonoBehaviour
{
    public enum State { Patrol, Chase, Search, Idle }
    public State state = State.Patrol;

    public enum Zone { Outside, House }
    public Zone currentZone = Zone.Outside;

    [Header("Refs")]
    public Transform player; // auto-found if empty
    public Transform eyes;   // optional eye position for raycasts
    public SpriteRenderer faceRenderer; // optional visual state indicator

    [Header("Patrol")]
    public Transform[] patrolPoints;
    public float waypointWaitTime = 1.5f;
    int patrolIndex = 0;
    float waitTimer = 0f;

    [Header("Movement")]
    public float patrolSpeed = 2.2f;
    public float chaseSpeed = 3.8f;
    public float turnSpeed = 120f;

    [Header("Detection")]
    public float viewDistance = 15f; // default, used if zone masks not set
    public float fieldOfView = 90f; // half-angle
    public float catchDistance = 1.6f;
    public float loseSightTime = 3f;
    [Header("Chase Tracking")]
    public float chaseExactTime = 1.2f; // Time in chase mode to know exact player position
    float chaseExactTimer = 0f;
    public LayerMask obstacleMask = ~0; // which blocks vision

    [Header("Zones (Layers)")]
    public LayerMask outsideLayers; // e.g. AI_Outside
    public LayerMask houseLayers;   // e.g. AI_House (neighbor's house)
    [Header("Safe Layer")]
    public LayerMask safeLayers; // layers that make the player invisible to AI when set

    [Header("Zone Tuning")]
    public float outsideViewDistance = 18f;
    public float houseViewDistance = 14f;
    public float outsideChaseSpeed = 4.2f;
    public float houseChaseSpeed = 3.8f;

    [Header("Search")]
    public float searchDuration = 6f;
    public float searchRadius = 4f;
    public int searchHops = 4;
    [Header("Idle")]
    public bool enableIdle = true;
    public float idleRadius = 6f;
    public float idleWalkSpeed = 1.6f;
    public float idleWaitTime = 2f;
    [Tooltip("Chance per second to enter idle while in-house and not suspicious")]
    public float idleEnterChancePerSecond = 0.12f;
    public Sprite idleSprite;
    Vector3 idleTarget = Vector3.zero;
    bool hasIdleTarget = false;
    float idleWaitTimer = 0f;
    [Header("House Search")]
    public float houseSearchRadius = 10f;
    public int houseSearchPoints = 8;

    [Header("Smart Patrol")]
    public bool autoPatrol = true;
    public int autoPatrolPoints = 4;
    public float autoPatrolRadiusOutside = 12f;
    public float autoPatrolRadiusHouse = 8f;
    public float autoPatrolReplanInterval = 20f;
    public float replanJitter = 5f;
    float autoPatrolTimer = 0f;
    Zone desiredPatrolZone = Zone.Outside;

    [Header("Point Placement Tuning")]
    public float minPointSpacing = 3f; // minimum distance between generated points
    public float openAreaCheckRadius = 6f; // radius to test how open area is
    public int openAreaSamples = 12; // directions to sample when scoring open area
    public int candidateMultiplier = 12; // how many candidate samples per needed point

    [Header("Audio")]
    public AudioSource musicSource;
    public AudioSource sfxSource;
    public AudioClip spottedSfx;
    public AudioClip chaseMusic;

    [Header("Sprites")]
    public Sprite patrolSprite;
    public Sprite chaseSprite;
    public Sprite searchSprite;

    [Header("Hearing")]
    public bool enableHearing = true;
    public float hearingRange = 12f; // Max distance AI can hear
    public float investigateCooldown = 1.0f;
    float lastInvestigateTime = -10f;

    [Header("Camera Effects")]
    // smaller default terror radius so effect triggers closer to the AI
    public float terrorRadius = 5f;
    public float maxShakeMagnitude = 0.15f; // local-position shake magnitude
    public float shakeLerpSpeed = 8f;
    public float cameraRefreshInterval = 5f; // how often to re-scan cameras
    float cameraRefreshTimer = 0f;
    public float shakeFullDistance = 5f; // full shake at this distance or closer
    public float shakeZeroDistance = 10f; // no shake beyond this distance
    public bool terrorRequireLOS = false; // if true, terror only triggers when AI or player can see each other

    [Header("Memory & Patrol Bias")]
    public float chaseMusicPersistTime = 4f; // how long chase music persists after last seen
    public float prioritizeSeenRadius = 12f; // radius to find nearby patrol points
    public float prioritizeSeenDuration = 10f; // for how long after seeing player to bias patrol
    public float prioritizeCooldown = 6f; // cooldown between reprioritizations
    float lastPrioritizeTime = -100f;

    class CamState { public Camera cam; public float originalFOV; public Vector3 originalLocalPos; }
    List<CamState> camStates = new List<CamState>();

    // Door tracking for closing after pass-through
    Door lastOpenedDoor;
    Vector3 lastDoorPos;
    Vector3 lastDoorForward;
    float doorCloseDelay = 0.75f;
    float doorCloseTimer = 0f;

    NavMeshAgent agent;
    Vector3 lastKnownPlayerPos;
    float timeSinceSeen = Mathf.Infinity;
    float searchTimer = 0f;
    int searchHopIndex = 0;
    // For intercept prediction
    public float interceptLeadTime = 1.0f;
    public float interceptMinDistance = 2f; // if very close, chase directly
    Vector3 lastPlayerPosition = Vector3.zero;
    Vector3 playerVelocity = Vector3.zero;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (player == null)
        {
            var pc = FindObjectOfType<PlayerController>();
            if (pc != null) player = pc.transform;
        }
        if (eyes == null) eyes = transform;
        UpdateFaceSprite();
        RefreshCameraList();
    }

    void OnEnable()
    {
        NoiseSystem.OnNoise += OnNoiseHeard;
    }

    void OnDisable()
    {
        NoiseSystem.OnNoise -= OnNoiseHeard;
    }

    void Start()
    {
        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            patrolIndex = 0;
            GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
        }
        else if (autoPatrol)
        {
            desiredPatrolZone = Zone.Outside;
            GeneratePatrolRoute(desiredPatrolZone);
        }
    }

    void Update()
    {
        if (player == null) return;

        UpdateZone();

        bool couldSee = timeSinceSeen == 0f; // previous frame indicator
        bool canSee = CanSeePlayer(out Vector3 seenPos);
        bool playerInZone = IsPlayerInReachableZone();

        // compute player velocity for interception (simple finite difference)
        if (Time.deltaTime > 0f && lastPlayerPosition != Vector3.zero)
        {
            playerVelocity = (player.position - lastPlayerPosition) / Time.deltaTime;
        }
        else
        {
            playerVelocity = Vector3.zero;
        }

        // If player is outside AI's reachable zone, immediately stop chase
        if (state == State.Chase && !playerInZone)
        {
            state = State.Patrol;
            UpdateFaceSprite();
            StopChaseMusic();
            timeSinceSeen = Mathf.Infinity;
            chaseExactTimer = 0f;
            return;
        }

        if (state == State.Chase)
        {
            if (canSee)
            {
                chaseExactTimer = chaseExactTime;
                lastKnownPlayerPos = seenPos;
                timeSinceSeen = 0f;
                if (!couldSee)
                {
                    OnSpottedPlayer();
                }
            }
            else
            {
                timeSinceSeen += Time.deltaTime;
                chaseExactTimer -= Time.deltaTime;
                if (chaseExactTimer < 0f) chaseExactTimer = 0f;
            }

            // --- FIX: Persist chase music as long as state is Chase ---
            if (musicSource != null && chaseMusic != null)
            {
                if (!musicSource.isPlaying || musicSource.clip != chaseMusic)
                {
                    StartChaseMusic();
                }
            }
            // Stop music after the configured persist time since last seen
            if (timeSinceSeen > chaseMusicPersistTime)
            {
                StopChaseMusic();
            }
        }
        else
        {
            if (canSee)
            {
                lastKnownPlayerPos = seenPos;
                timeSinceSeen = 0f;
                if (!couldSee)
                {
                    OnSpottedPlayer();
                }
            }
            else
            {
                timeSinceSeen += Time.deltaTime;
            }
        }

        switch (state)
        {
            case State.Patrol:
                UpdatePatrol(canSee);
                break;
            case State.Chase:
                UpdateChase(canSee);
                break;
            case State.Search:
                UpdateSearch(canSee);
                break;
            case State.Idle:
                UpdateIdle(canSee);
                break;
        }

        // If we have a last known player position and it lies inside the House zone,
        // force Search mode so the AI will comb interior points.
        if (lastKnownPlayerPos != Vector3.zero)
        {
            try
            {
                if (ZoneFromPosition(lastKnownPlayerPos) == Zone.House && state != State.Chase)
                {
                    if (state != State.Search)
                    {
                        state = State.Search;
                        UpdateFaceSprite();
                        searchTimer = 0f;
                        searchHopIndex = 0;
                        GenerateInteriorSearchPoints(lastKnownPlayerPos);
                        if (patrolPoints != null && patrolPoints.Length > 0)
                        {
                            GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
                        }
                    }
                }
            }
            catch { }
        }

        // Camera terror / shake effects based on distance (and optionally LOS)
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        bool visibleForTerror = true;
        if (terrorRequireLOS)
        {
            // require either AI can see player, or player can see AI
            visibleForTerror = canSee || PlayerCanSeeAI();
        }

        float shakeIntensity = 0f;
        if (state == State.Chase)
        {
            shakeIntensity = 1f;
        }
        else if (visibleForTerror)
        {
            if (distToPlayer <= shakeFullDistance) shakeIntensity = 1f;
            else if (distToPlayer >= shakeZeroDistance) shakeIntensity = 0f;
            else shakeIntensity = 1f - ((distToPlayer - shakeFullDistance) / (shakeZeroDistance - shakeFullDistance));
        }

        UpdateCameraShake(shakeIntensity);

        // Try opening doors when moving
        TryOpenDoorAhead();
        TryCloseLastDoor();

        // Catch check
        // Catch check (don't catch if player is on safe layer)
        if (!IsPlayerOnSafeLayer() && Vector3.Distance(transform.position, player.position) <= catchDistance)
        {
            OnCatchPlayer();
        }

        // remember player position for next-frame velocity estimate
        lastPlayerPosition = player.position;
    }

    // Returns true if player is in a zone the AI can reach (same as currentZone)
    bool IsPlayerInReachableZone()
    {
        if (IsPlayerOnSafeLayer()) return false;
        Vector3 playerPos = player.position + Vector3.up * 0.5f;
        if (Physics.Raycast(playerPos, Vector3.down, out RaycastHit hit, 3f, outsideLayers | houseLayers, QueryTriggerInteraction.Ignore))
        {
            var playerZone = ZoneFromLayer(hit.collider.gameObject.layer);
            // AI can only reach player if in the same zone
            return playerZone == currentZone;
        }
        return false;
    }

    void UpdatePatrol(bool canSee)
    {
        if (canSee)
        {
            state = State.Chase;
            GoTo(lastKnownPlayerPos, chaseSpeed);
            return;
        }

        // Occasionally enter idle mode when inside the house and not suspicious
        if (enableIdle && currentZone == Zone.House && !canSee && state != State.Idle)
        {
            if (Random.value < idleEnterChancePerSecond * Time.deltaTime)
            {
                state = State.Idle;
                hasIdleTarget = false;
                idleWaitTimer = 0f;
                UpdateFaceSprite();
                return;
            }
        }

        // If we've recently seen the player inside a nearby area, prioritize patrol points near that location
        if (lastKnownPlayerPos != Vector3.zero && timeSinceSeen < prioritizeSeenDuration && Time.time - lastPrioritizeTime > prioritizeCooldown && patrolPoints != null && patrolPoints.Length > 0)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                float d = Vector3.Distance(patrolPoints[i].position, lastKnownPlayerPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            if (best != -1 && bestDist <= prioritizeSeenRadius)
            {
                patrolIndex = best;
                GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
                lastPrioritizeTime = Time.time;
                return;
            }
        }

        // Auto patrol planning
        if ((patrolPoints == null || patrolPoints.Length == 0) && autoPatrol)
        {
            GeneratePatrolRoute(desiredPatrolZone);
        }

        // Periodically re-plan and occasionally switch zones
        if (autoPatrol)
        {
            autoPatrolTimer += Time.deltaTime;
            float nextPlan = autoPatrolReplanInterval + Random.Range(-replanJitter, replanJitter);
            if (autoPatrolTimer >= nextPlan)
            {
                // With a small chance, switch zones
                if (Random.value < 0.35f)
                {
                    desiredPatrolZone = desiredPatrolZone == Zone.Outside ? Zone.House : Zone.Outside;
                }
                GeneratePatrolRoute(desiredPatrolZone);
                autoPatrolTimer = 0f;
            }
        }

        if (patrolPoints == null || patrolPoints.Length == 0) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= waypointWaitTime)
            {
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
                GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
                waitTimer = 0f;
            }
        }
    }

    void UpdateChase(bool canSee)
    {
        agent.speed = GetZoneChaseSpeed();

        // Chase: attempt to intercept/cut-off the player using a simple linear prediction
        float distToPlayer = Vector3.Distance(transform.position, player.position);

        // If very close, go directly to player
        if (distToPlayer <= interceptMinDistance)
        {
            GoTo(player.position, chaseSpeed);
        }
        else
        {
            Vector3 predicted = player.position + playerVelocity * interceptLeadTime;
            // Limit vertical prediction to reasonable bounds
            predicted.y = Mathf.Clamp(predicted.y, player.position.y - 1f, player.position.y + 1.5f);
            // Try to sample a NavMesh position near predicted point
            if (NavMesh.SamplePosition(predicted, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                GoTo(hit.position, chaseSpeed);
            }
            else
            {
                // Fallback to last known position
                if (chaseExactTimer > 0f)
                {
                    GoTo(player.position, chaseSpeed);
                }
                else
                {
                    GoTo(lastKnownPlayerPos, chaseSpeed);
                }
            }
        }

        if (timeSinceSeen > loseSightTime)
        {
            state = State.Search;
            UpdateFaceSprite();
            StopChaseMusic();
            searchTimer = 0f;
            searchHopIndex = 0;
            GoTo(lastKnownPlayerPos, patrolSpeed);
        }
    }

    void UpdateSearch(bool canSee)
    {
        if (canSee)
        {
            state = State.Chase;
            GoTo(lastKnownPlayerPos, chaseSpeed);
            return;
        }

        searchTimer += Time.deltaTime;
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            if (searchHopIndex < searchHops)
            {
                var rnd = Random.insideUnitCircle * searchRadius;
                Vector3 dest = lastKnownPlayerPos + new Vector3(rnd.x, 0f, rnd.y);
                if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    GoTo(hit.position, patrolSpeed);
                    searchHopIndex++;
                }
            }
        }

        if (searchTimer >= searchDuration)
        {
            state = State.Patrol;
            UpdateFaceSprite();
            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
            }
        }
    }

    void UpdateIdle(bool canSee)
    {
        if (canSee)
        {
            state = State.Chase;
            GoTo(lastKnownPlayerPos, chaseSpeed);
            UpdateFaceSprite();
            return;
        }

        // If AI got suspicious (recently saw or heard player), leave idle
        if (timeSinceSeen < Mathf.Infinity && timeSinceSeen < loseSightTime)
        {
            state = State.Search;
            UpdateFaceSprite();
            searchTimer = 0f;
            searchHopIndex = 0;
            return;
        }

        if (!hasIdleTarget)
        {
            // Prefer sampling across whole house if inside
            Vector3 sampleCenter = transform.position;
            Bounds hb;
            if (currentZone == Zone.House && TryGetHouseBounds(transform.position, Mathf.Max(houseSearchRadius, 8f), out hb))
            {
                sampleCenter = hb.center;
            }

            Vector3 samplePos;
            if (currentZone == Zone.House && TryGetHouseBounds(sampleCenter, Mathf.Max(houseSearchRadius, 8f), out hb))
            {
                float sx = Random.Range(hb.min.x, hb.max.x);
                float sz = Random.Range(hb.min.z, hb.max.z);
                float sy = Random.Range(hb.min.y + 0.5f, hb.max.y - 0.5f);
                samplePos = new Vector3(sx, sy, sz);
            }
            else
            {
                Vector2 r = Random.insideUnitCircle * idleRadius;
                samplePos = transform.position + new Vector3(r.x, 0f, r.y);
            }

            if (NavMesh.SamplePosition(samplePos, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                idleTarget = hit.position;
                hasIdleTarget = true;
                GoTo(idleTarget, idleWalkSpeed);
            }
            else
            {
                hasIdleTarget = false;
            }
        }
        else
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
            {
                idleWaitTimer += Time.deltaTime;
                if (idleWaitTimer >= idleWaitTime)
                {
                    hasIdleTarget = false;
                    idleWaitTimer = 0f;
                    // small chance to return to patrol
                    if (patrolPoints != null && patrolPoints.Length > 0 && Random.value < 0.5f)
                    {
                        state = State.Patrol;
                        UpdateFaceSprite();
                        GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
                    }
                }
            }
        }
    }

    void OnNoiseHeard(Vector3 pos, float radius)
    {
        if (!enableHearing) return;

        // If this is a global / emergency noise (e.g. glass break) signaled by infinite radius,
        // always process it immediately regardless of distance or cooldown so AI always knows the location.
        if (radius == float.PositiveInfinity)
        {
            lastInvestigateTime = Time.time;
            lastKnownPlayerPos = pos;
            // If currently chasing, keep chasing but remember event; otherwise switch to search and investigate
            if (state != State.Chase)
            {
                state = State.Search;
                UpdateFaceSprite();
                searchTimer = 0f;
                searchHopIndex = 0;
                GoTo(pos, patrolSpeed);
            }
            return;
        }

        // Ignore if recently reacted
        if (Time.time - lastInvestigateTime < investigateCooldown) return;

        float dist = Vector3.Distance(transform.position, pos);
        if (dist > hearingRange || dist > radius) return;

        // Only investigate noises when not currently chasing/searching
        if (state != State.Patrol && state != State.Idle) return;

        // If already seeing the player, ignore noise
        if (timeSinceSeen == 0f) return;

        // If the noise comes from the player and the player is on a safe layer, ignore it
        if (player != null && IsPlayerOnSafeLayer() && Vector3.Distance(pos, player.position) < 1f) return;

        lastInvestigateTime = Time.time;
        lastKnownPlayerPos = pos;
        state = State.Search;
        UpdateFaceSprite();
        searchTimer = 0f;
        searchHopIndex = 0;
        GoTo(pos, patrolSpeed);
    }

    void TryOpenDoorAhead()
    {
        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * 1.2f;
        Vector3 dir = (agent.hasPath && agent.desiredVelocity.sqrMagnitude > 0.01f)
            ? agent.desiredVelocity.normalized
            : (eyes != null ? eyes.forward : transform.forward);
        float range = 1.6f;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, range))
        {
            var door = hit.collider.GetComponentInParent<Door>();
            if (door != null && !door.isLocked && !door.isOpen)
            {
                door.Interact(null); // open
                lastOpenedDoor = door;
                lastDoorPos = door.transform.position;
                lastDoorForward = door.transform.forward;
                doorCloseTimer = doorCloseDelay;
            }
        }
    }

    void TryCloseLastDoor()
    {
        if (lastOpenedDoor == null) return;
        doorCloseTimer -= Time.deltaTime;
        // Close when AI has passed the door plane and moved a bit beyond
        float side = Vector3.Dot(transform.position - lastDoorPos, lastDoorForward);
        float distance = Vector3.Distance(transform.position, lastDoorPos);
        if (side > 0.3f && distance > 1.5f && doorCloseTimer <= 0f)
        {
            lastOpenedDoor.isOpen = false;
            lastOpenedDoor = null;
        }
    }

    void GeneratePatrolRoute(Zone zone)
    {
        List<Transform> pts = new List<Transform>();
        float radius = zone == Zone.House ? autoPatrolRadiusHouse : autoPatrolRadiusOutside;
        Vector3 center = transform.position;

        int needed = Mathf.Max(2, autoPatrolPoints);
        int candidateCount = Mathf.Max(needed * candidateMultiplier, needed * 4);

        List<(Vector3 pos, float score)> candidates = new List<(Vector3, float)>();
        int attempts = 0;
        while (candidates.Count < candidateCount && attempts < candidateCount * 3)
        {
            attempts++;
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(r.x, 0f, r.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;

            // verify zone by raycast down
            Zone czone = currentZone;
            Vector3 check = hit.position + Vector3.up * 0.5f;
            if (Physics.Raycast(check, Vector3.down, out RaycastHit ground, 3f, outsideLayers | houseLayers, QueryTriggerInteraction.Ignore))
            {
                czone = ZoneFromLayer(ground.collider.gameObject.layer);
            }
            if (czone != zone) continue;

            float score = ScoreOpenArea(hit.position);
            candidates.Add((hit.position, score));
        }

        // Sort candidates by openness score descending
        candidates.Sort((a, b) => b.score.CompareTo(a.score));

        // Greedily pick points ensuring min spacing
        for (int i = 0; i < candidates.Count && pts.Count < needed; i++)
        {
            Vector3 p = candidates[i].pos;
            bool ok = true;
            for (int j = 0; j < pts.Count; j++) if (Vector3.Distance(pts[j].position, p) < minPointSpacing) { ok = false; break; }
            if (!ok) continue;
            var go = new GameObject($"PatrolPoint_{pts.Count}");
            go.transform.position = p;
            pts.Add(go.transform);
        }

        // Fallback: if not enough, add additional NavMesh samples anywhere, respecting spacing
        attempts = 0;
        while (pts.Count < needed && attempts < needed * 20)
        {
            attempts++;
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(r.x, 0f, r.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            bool ok = true;
            for (int j = 0; j < pts.Count; j++) if (Vector3.Distance(pts[j].position, hit.position) < minPointSpacing) { ok = false; break; }
            if (!ok) continue;
            var go = new GameObject($"PatrolPoint_fb_{pts.Count}");
            go.transform.position = hit.position;
            pts.Add(go.transform);
        }

        patrolPoints = pts.ToArray();
        patrolIndex = 0;
        waitTimer = 0f;
        if (patrolPoints.Length > 0)
        {
            GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
        }
    }

    float ScoreOpenArea(Vector3 pos)
    {
        Vector3 origin = pos + Vector3.up * 0.5f;
        float sum = 0f;
        for (int i = 0; i < openAreaSamples; i++)
        {
            float ang = (360f / openAreaSamples) * i;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, openAreaCheckRadius, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                sum += hit.distance;
            }
            else
            {
                sum += openAreaCheckRadius;
            }
        }
        float avg = sum / openAreaSamples;
        return Mathf.Clamp01(avg / openAreaCheckRadius);
    }

    bool TryGetHouseBounds(Vector3 center, float searchRadius, out Bounds bounds)
    {
        bounds = new Bounds(center, Vector3.zero);
        Collider[] cols = Physics.OverlapSphere(center, searchRadius, houseLayers, QueryTriggerInteraction.Ignore);
        if (cols == null || cols.Length == 0) return false;
        bool first = true;
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;
            if (first)
            {
                bounds = c.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(c.bounds);
            }
        }
        return !first;
    }

    bool CanSeePlayer(out Vector3 seenPos)
    {
        seenPos = Vector3.zero;
        if (IsPlayerOnSafeLayer()) return false;
        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * 1.6f;
        Vector3 toPlayer = player.position - origin;
        float dist = toPlayer.magnitude;
        float vd = GetZoneViewDistance();
        if (dist > vd) return false;

        Vector3 dir = toPlayer.normalized;
        float angle = Vector3.Angle(eyes != null ? eyes.forward : transform.forward, dir);
        if (angle > fieldOfView) return false;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, vd, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == player || hit.collider.GetComponentInParent<PlayerController>() != null)
            {
                seenPos = player.position;
                return true;
            }
            return false;
        }

        // If nothing hit, assume clear line (rare when obstacleMask excludes default)
        seenPos = player.position;
        return true;
    }

    void GoTo(Vector3 position, float speed)
    {
        agent.speed = speed;
        agent.isStopped = false;
        agent.SetDestination(position);
    }

    float lastSpotSfxTime = -10f;
    float spotSfxCooldown = 1.0f;

    void OnSpottedPlayer()
    {
        if (IsPlayerOnSafeLayer()) return;
        if (state != State.Chase)
        {
            state = State.Chase;
            UpdateFaceSprite();
        }
        // Only play the spot SFX if not already in grace period (i.e., just saw player or grace expired)
        if (Time.time - lastSpotSfxTime > spotSfxCooldown)
        {
            PlaySpottedSfx();
            lastSpotSfxTime = Time.time;
        }
        StartChaseMusic();
    }

    void UpdateZone()
    {
        // Determine zone by checking layer of ground or surrounding colliders
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3f, outsideLayers | houseLayers, QueryTriggerInteraction.Ignore))
        {
            currentZone = ZoneFromLayer(hit.collider.gameObject.layer);
            return;
        }

        // Fallback: overlap sphere
        Collider[] cols = Physics.OverlapSphere(transform.position, 0.6f, outsideLayers | houseLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < cols.Length; i++)
        {
            currentZone = ZoneFromLayer(cols[i].gameObject.layer);
            return;
        }

        currentZone = Zone.Outside;
    }

    Zone ZoneFromLayer(int layer)
    {
        int bit = 1 << layer;
        if ((houseLayers.value & bit) != 0) return Zone.House;
        if ((outsideLayers.value & bit) != 0) return Zone.Outside;
        return Zone.Outside;
    }

    bool IsPlayerOnSafeLayer()
    {
        if (player == null) return false;
        int bit = 1 << player.gameObject.layer;
        return (safeLayers.value & bit) != 0;
    }

    // Determine zone by sampling down at a specific world position
    Zone ZoneFromPosition(Vector3 pos)
    {
        Vector3 origin = pos + Vector3.up * 0.5f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3f, outsideLayers | houseLayers, QueryTriggerInteraction.Ignore))
        {
            return ZoneFromLayer(hit.collider.gameObject.layer);
        }
        Collider[] cols = Physics.OverlapSphere(pos, 0.6f, outsideLayers | houseLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < cols.Length; i++)
        {
            return ZoneFromLayer(cols[i].gameObject.layer);
        }
        return Zone.Outside;
    }

    void GenerateInteriorSearchPoints(Vector3 center)
    {
        List<Transform> pts = new List<Transform>();
        int needed = Mathf.Max(2, houseSearchPoints);
        int candidateCount = Mathf.Max(needed * candidateMultiplier, needed * 4);

        List<(Vector3 pos, float score)> candidates = new List<(Vector3, float)>();
        int attempts = 0;
        // Try to get combined house bounds near the center to sample across entire building (including upstairs)
        Bounds houseBounds;
        bool haveBounds = TryGetHouseBounds(center, Mathf.Max(houseSearchRadius, 8f), out houseBounds);

        while (candidates.Count < candidateCount && attempts < candidateCount * 3)
        {
            attempts++;
            Vector3 samplePos;
            if (haveBounds)
            {
                float sx = Random.Range(houseBounds.min.x, houseBounds.max.x);
                float sz = Random.Range(houseBounds.min.z, houseBounds.max.z);
                float sy = Random.Range(houseBounds.min.y + 0.5f, houseBounds.max.y - 0.5f);
                samplePos = new Vector3(sx, sy, sz);
            }
            else
            {
                Vector2 r = Random.insideUnitCircle * houseSearchRadius;
                samplePos = center + new Vector3(r.x, 0f, r.y);
            }
            if (!NavMesh.SamplePosition(samplePos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            if (ZoneFromPosition(hit.position) != Zone.House) continue;
            float score = ScoreOpenArea(hit.position);
            candidates.Add((hit.position, score));
        }

        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        for (int i = 0; i < candidates.Count && pts.Count < needed; i++)
        {
            Vector3 p = candidates[i].pos;
            bool ok = true;
            for (int j = 0; j < pts.Count; j++) if (Vector3.Distance(pts[j].position, p) < minPointSpacing) { ok = false; break; }
            if (!ok) continue;
            var go = new GameObject($"SearchPoint_{pts.Count}");
            go.transform.position = p;
            pts.Add(go.transform);
        }

        attempts = 0;
        while (pts.Count < needed && attempts < needed * 20)
        {
            attempts++;
            Vector2 r = Random.insideUnitCircle * houseSearchRadius;
            Vector3 candidate = center + new Vector3(r.x, 0f, r.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            bool ok = true;
            for (int j = 0; j < pts.Count; j++) if (Vector3.Distance(pts[j].position, hit.position) < minPointSpacing) { ok = false; break; }
            if (!ok) continue;
            var go = new GameObject($"SearchPoint_fb_{pts.Count}");
            go.transform.position = hit.position;
            pts.Add(go.transform);
        }

        patrolPoints = pts.ToArray();
        patrolIndex = 0;
        waitTimer = 0f;
    }

    float GetZoneViewDistance()
    {
        switch (currentZone)
        {
            case Zone.House: return houseViewDistance;
            case Zone.Outside: return outsideViewDistance;
            default: return viewDistance;
        }
    }

    float GetZoneChaseSpeed()
    {
        switch (currentZone)
        {
            case Zone.House: return houseChaseSpeed;
            case Zone.Outside: return outsideChaseSpeed;
            default: return chaseSpeed;
        }
    }

    void OnCatchPlayer()
    {
        // Simple response: restart scene via GameManager if present
        var gm = FindObjectOfType<GameManager>();
        if (gm != null)
        {
            StartCoroutine(RestartAfter(1f));
        }
        else
        {
            // Fallback: move player to a point far away (simple reset)
            Vector3 away = (transform.position - player.position).normalized * 3f;
            player.position = transform.position + away + Vector3.up * 0.5f;
        }
    }

    IEnumerator RestartAfter(float t)
    {
        agent.isStopped = true;
        yield return new WaitForSeconds(t);
        var gm = FindObjectOfType<GameManager>();
        if (gm != null) gm.Restart();
    }

    void PlaySpottedSfx()
    {
        if (sfxSource != null && spottedSfx != null)
        {
            sfxSource.PlayOneShot(spottedSfx);
        }
    }

    void StartChaseMusic()
    {
        if (musicSource != null && chaseMusic != null)
        {
            if (!musicSource.isPlaying || musicSource.clip != chaseMusic)
            {
                musicSource.clip = chaseMusic;
                musicSource.loop = true;
                musicSource.Play();
            }
        }
    }

    void StopChaseMusic()
    {
        if (musicSource != null && musicSource.clip == chaseMusic)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }
    }

    void UpdateFaceSprite()
    {
        if (faceRenderer == null) return;
        switch (state)
        {
            case State.Patrol:
                faceRenderer.sprite = patrolSprite;
                break;
            case State.Chase:
                faceRenderer.sprite = chaseSprite;
                break;
            case State.Search:
                faceRenderer.sprite = searchSprite;
                break;
            case State.Idle:
                faceRenderer.sprite = idleSprite != null ? idleSprite : patrolSprite;
                break;
        }
    }

    void RefreshCameraList()
    {
        camStates.Clear();
        Camera[] cams = Camera.allCameras;
        for (int i = 0; i < cams.Length; i++)
        {
            var c = cams[i];
            if (c == null) continue;
            CamState s = new CamState { cam = c, originalFOV = c.fieldOfView, originalLocalPos = c.transform.localPosition };
            camStates.Add(s);
        }
    }

    void RestoreCameras()
    {
        for (int i = 0; i < camStates.Count; i++)
        {
            var s = camStates[i];
            if (s == null || s.cam == null) continue;
            s.cam.fieldOfView = s.originalFOV;
            if (s.cam.transform != null) s.cam.transform.localPosition = s.originalLocalPos;
        }
    }

    void UpdateCameraShake(float shakeIntensity)
    {
        cameraRefreshTimer += Time.deltaTime;
        if (cameraRefreshTimer >= cameraRefreshInterval)
        {
            RefreshCameraList();
            cameraRefreshTimer = 0f;
        }

        // If there's no shake intensity, immediately restore original camera local positions
        if (shakeIntensity <= 0f)
        {
            for (int i = 0; i < camStates.Count; i++)
            {
                var s = camStates[i];
                if (s == null || s.cam == null) continue;
                if (s.cam.transform != null) s.cam.transform.localPosition = s.originalLocalPos;
            }
            return;
        }

        for (int i = 0; i < camStates.Count; i++)
        {
            var s = camStates[i];
            if (s == null || s.cam == null) continue;
            Vector3 targetPos = s.originalLocalPos + Random.insideUnitSphere * maxShakeMagnitude * shakeIntensity;
            s.cam.transform.localPosition = Vector3.Lerp(s.cam.transform.localPosition, targetPos, Time.deltaTime * shakeLerpSpeed);
        }
    }

    bool PlayerCanSeeAI()
    {
        if (player == null) return false;
        if (IsPlayerOnSafeLayer()) return false;
        Camera pc = Camera.main;
        if (pc == null) return false;
        Vector3 playerEye = pc.transform.position;
        Vector3 target = eyes != null ? eyes.position : transform.position + Vector3.up * 1.6f;
        Vector3 dir = target - playerEye;
        float dist = dir.magnitude;
        if (dist <= 0.01f) return true;
        dir.Normalize();
        if (Physics.Raycast(playerEye, dir, out RaycastHit hit, dist, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            // if ray hit something before reaching the AI, player does not see AI
            if (hit.transform == transform || hit.collider.GetComponentInParent<Adaai>() != null) return true;
            return false;
        }
        return true;
    }

    void OnDestroy()
    {
        RestoreCameras();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 o = eyes != null ? eyes.position : transform.position + Vector3.up * 1.6f;
        Gizmos.DrawWireSphere(o, viewDistance);
        // FOV gizmos
        Vector3 fwd = eyes != null ? eyes.forward : transform.forward;
        Quaternion left = Quaternion.AngleAxis(-fieldOfView, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(fieldOfView, Vector3.up);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawRay(o, left * fwd * 2f);
        Gizmos.DrawRay(o, right * fwd * 2f);

        // Patrol points
        if (patrolPoints != null && patrolPoints.Length > 1)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                Gizmos.DrawSphere(patrolPoints[i].position, 0.15f);
                var next = patrolPoints[(i + 1) % patrolPoints.Length];
                if (next != null) Gizmos.DrawLine(patrolPoints[i].position, next.position);
            }
        }
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Create BearTrap Prefab (From Adaai)")]
    public static void CreateBearTrapPrefabFromAdaai()
    {
        string dir = "Assets/Resources/Prefabs";
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "BearTrap.prefab");

        GameObject root = new GameObject("BearTrap_Temp");
        var trap = root.AddComponent<BearTrap>();

        // Create a simple visual child (do NOT assign this renderer into the trap component)
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "TrapVisual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = new Vector3(0.6f, 0.1f, 0.6f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());

        // Add a trigger collider on the root for detecting steps
        var col = root.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.center = Vector3.up * 0.05f;
        col.size = new Vector3(0.6f, 0.1f, 0.6f);

        // Add a kinematic rigidbody so physics queries behave correctly if needed
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
        if (prefab != null)
        {
            Debug.Log("Created BearTrap prefab at " + path + " — assign `targetRenderer` and materials on the prefab asset.");
        }
        else
        {
            Debug.LogError("Failed to create BearTrap prefab at " + path);
        }

        Object.DestroyImmediate(root);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
    }
#endif
}
