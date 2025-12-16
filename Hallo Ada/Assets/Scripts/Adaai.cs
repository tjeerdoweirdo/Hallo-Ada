using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class Adaai : MonoBehaviour
{
    public enum State { Patrol, Chase, Search }
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
    public LayerMask obstacleMask = ~0; // which blocks vision

    [Header("Zones (Layers)")]
    public LayerMask outsideLayers; // e.g. AI_Outside
    public LayerMask houseLayers;   // e.g. AI_House (neighbor's house)

    [Header("Zone Tuning")]
    public float outsideViewDistance = 18f;
    public float houseViewDistance = 14f;
    public float outsideChaseSpeed = 4.2f;
    public float houseChaseSpeed = 3.8f;

    [Header("Search")]
    public float searchDuration = 6f;
    public float searchRadius = 4f;
    public int searchHops = 4;

    [Header("Smart Patrol")]
    public bool autoPatrol = true;
    public int autoPatrolPoints = 4;
    public float autoPatrolRadiusOutside = 12f;
    public float autoPatrolRadiusHouse = 8f;
    public float autoPatrolReplanInterval = 20f;
    public float replanJitter = 5f;
    float autoPatrolTimer = 0f;
    Zone desiredPatrolZone = Zone.Outside;

    [Header("Audio")]
    public AudioSource musicSource;
    public AudioSource sfxSource;
    public AudioClip spottedSfx;
    public AudioClip chaseMusic;

    [Header("Sprites")]
    public Sprite patrolSprite;
    public Sprite chaseSprite;
    public Sprite searchSprite;

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
        }

        // Try opening doors when moving
        TryOpenDoorAhead();
        TryCloseLastDoor();

        // Catch check
        if (Vector3.Distance(transform.position, player.position) <= catchDistance)
        {
            OnCatchPlayer();
        }
    }

    void UpdatePatrol(bool canSee)
    {
        if (canSee)
        {
            state = State.Chase;
            GoTo(lastKnownPlayerPos, chaseSpeed);
            return;
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

        if (canSee)
        {
            GoTo(lastKnownPlayerPos, chaseSpeed);
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

        int attempts = 0;
        int needed = Mathf.Max(2, autoPatrolPoints);
        while (pts.Count < needed && attempts < needed * 10)
        {
            attempts++;
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(r.x, 0f, r.y);
            // Bias toward the chosen zone by sampling near geometry with that layer
            // Sample on NavMesh
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                // Verify zone by raycast down
                Zone czone = currentZone;
                Vector3 check = hit.position + Vector3.up * 0.5f;
                if (Physics.Raycast(check, Vector3.down, out RaycastHit ground, 3f, outsideLayers | houseLayers, QueryTriggerInteraction.Ignore))
                {
                    czone = ZoneFromLayer(ground.collider.gameObject.layer);
                }
                if (czone == zone)
                {
                    var go = new GameObject($"PatrolPoint_{pts.Count}");
                    go.transform.position = hit.position;
                    pts.Add(go.transform);
                }
            }
        }

        // If we couldn't satisfy zone strictly, fallback to any valid points
        if (pts.Count < 2)
        {
            pts.Clear();
            for (int i = 0; i < needed; i++)
            {
                Vector2 r = Random.insideUnitCircle * radius;
                Vector3 candidate = center + new Vector3(r.x, 0f, r.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    var go = new GameObject($"PatrolPoint_{i}");
                    go.transform.position = hit.position;
                    pts.Add(go.transform);
                }
            }
        }

        patrolPoints = pts.ToArray();
        patrolIndex = 0;
        waitTimer = 0f;
        if (patrolPoints.Length > 0)
        {
            GoTo(patrolPoints[patrolIndex].position, patrolSpeed);
        }
    }

    bool CanSeePlayer(out Vector3 seenPos)
    {
        seenPos = Vector3.zero;
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

    void OnSpottedPlayer()
    {
        if (state != State.Chase)
        {
            state = State.Chase;
            UpdateFaceSprite();
        }
        PlaySpottedSfx();
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
        }
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
}
