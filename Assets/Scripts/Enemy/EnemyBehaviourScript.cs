using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyBehaviourScript : MonoBehaviour
{   
    [SerializeField] private Enemy enemy;
    public Weapon enemyWeapon;
    public Transform rootScanner, aimLookAt;
    public Rig aimLayer;
    private enum State {
        Idle,
        Patrol,
        Chase,
        Attack,
        Looking
    }
    public enum TypePatrol {
        MoveAround,
        StandInPlace
    }
    public TypePatrol typePatrol;
    public Vector3[] patrolList;
    [HideInInspector] public Vector3 standPos;
    [SerializeField] private Scanner playerScanner = new Scanner();
    private NavMeshAgent agent;
    private GameObject FieldOfView;
    private int patrolIndex = 0;
    private Vector3 playerPosition;
    private Transform player;
    private State state, prevState;
    private float IdleTimer;
    private bool isDeadBody;
    private bool isAlert;
    private Animator animator;
    private int velocityHash;
    private GameManager gameManager;
    private void Awake() {
        gameManager = GameManager.Instance;
        agent = GetComponent<NavMeshAgent>();
        agent.speed = enemy.speed;
        agent.angularSpeed = enemy.angularSpeed;
        agent.acceleration = enemy.acceleration;

        animator = GetComponent<Animator>();
        velocityHash = Animator.StringToHash("Velocity");

    }

    private void OnEnable() {

        gameManager.OnEnemyAlert.AddListener(HandleOnAlert);
        gameManager.OnEnemyAlertOff.AddListener(HandleOnAlertOff);

        playerScanner.OnDetectedTarget.AddListener(HandleWhenDetected);
        playerScanner.OnNotDetectedTarget.AddListener(HandleWhenNotDetected);
        playerScanner.OnDetectedSubTarget.AddListener(HandleWhenDetectedSubtarget);
        
    }

    // Start is called before the first frame update
    void Start()
    {
        //creata field of view
        FieldOfView = playerScanner.CreataFieldOfView(rootScanner, rootScanner.position, enemy.detectionAngle, enemy.viewDistance);

        // sửa lỗi khi stoppingDistance enemy không thể chuyển trạng thái sau khi chase
        if(agent.stoppingDistance == 0) {
            agent.stoppingDistance = 1;
        }
    }

    // Update is called once per frame
    void Update()
    {
        playerScanner.Scan(rootScanner);
        HandlAnimation();
        switch(state) {
            case State.Idle:
                prevState = state;
                Idle();
                break;
            case State.Patrol:
                prevState = state;
                Patrol();
                break;
            case State.Attack:
                prevState = state;
                IdleTimer = 0;
                Attack(playerPosition);
                break;
            case State.Chase:
                prevState = state;
                IdleTimer = 0;
                Chase(playerPosition);
                break;
            case State.Looking:
                Looking();
                break;
            default:
                break;
        }
    }

    private void Idle() {
        IdleTimer += Time.deltaTime;
        agent.SetDestination(transform.position);
        if(!isDeadBody) {
            if(IdleTimer >= enemy.IdleTime) {
                state = State.Patrol;
                IdleTimer = 0;
            }
        } else {
            if(IdleTimer >= 1) {
                gameManager.EnemyTriggerAlert(playerPosition, enemy.alertTime);
                IdleTimer = 0;
            }
        }
    }

    private void Patrol() {
        Vector3 walkPoint = patrolList[patrolIndex];
        switch(typePatrol) {
            case TypePatrol.MoveAround:
                if(agent.remainingDistance <= agent.stoppingDistance) {
                    IdleTimer += Time.deltaTime;
                    if(IdleTimer > enemy.IdleTime) {
                        patrolIndex++;
                        if(patrolIndex >= patrolList.Length) {
                            patrolIndex = 0;
                        }
                        agent.SetDestination(walkPoint);
                        IdleTimer = 0;
                    }
                }
                break;
            case TypePatrol.StandInPlace:
                    agent.SetDestination(standPos);
                    if(agent.remainingDistance <= agent.stoppingDistance) {
                        IdleTimer += Time.deltaTime;
                        transform.rotation = LerpRotation(walkPoint, transform.position, enemy.speedRotation);
                        if(IdleTimer > enemy.IdleTime) {
                            patrolIndex++;
                            if(patrolIndex >= patrolList.Length) {
                                patrolIndex = 0;
                            }
                            IdleTimer = 0;
                        }
                    }
                break;
            default:
                break;
        }
    }

    private void Attack(Vector3 playerPos) {
        agent.SetDestination(transform.position);
        Vector3 dirLook = playerPos - transform.position;
        Quaternion rotLook = Quaternion.LookRotation(dirLook.normalized);
        transform.rotation = Quaternion.Lerp(transform.rotation, rotLook, enemy.speedRotation * Time.deltaTime);
        aimLayer.weight += Time.deltaTime/0.1f;
        aimLookAt.position = playerPos;
        if(Mathf.Abs(Quaternion.Angle(transform.rotation, rotLook)) <= 20) {
            enemyWeapon.Attack(player, playerScanner.layerMaskTarget);
        }
    }

    private void Chase(Vector3 pos) {
        agent.SetDestination(pos);
        if(agent.remainingDistance != 0 && agent.remainingDistance <= agent.stoppingDistance) {
            if(isAlert) {
                state = State.Looking;
                return;
            }
            state = State.Idle;
        }
    }

    private void Looking() {
        if(agent.remainingDistance <= agent.stoppingDistance) {
            IdleTimer += Time.deltaTime;
            if(IdleTimer >= 0.5f) {
                Vector3 pos = RandomNavmeshLocation(agent.height * 2);
                agent.SetDestination(pos);
                IdleTimer = 0;
            }
        }
    }


    private Quaternion LerpRotation(Vector3 pos1, Vector3 pos2, float speed) {
        Vector3 dirLook = pos1 - pos2;
        Quaternion rotLook = Quaternion.LookRotation(dirLook.normalized);
        rotLook.x = 0;
        rotLook.z = 0;
        return Quaternion.Lerp(transform.rotation, rotLook, speed * Time.deltaTime);
    }
    
    public void HandleWhenDetected(List<RaycastHit> hitList) {
        player = playerScanner.DetectSingleTarget(hitList);
        playerPosition = player.position;
        state = State.Attack;
    }

    public void HandleWhenNotDetected() {
        aimLayer.weight -= Time.deltaTime/0.1f;
        if(prevState == State.Attack) {
            state = State.Chase;
        }
    }

    private void HandleWhenDetectedSubtarget(Transform _transform) {
        bool isDetected = _transform.GetComponentInParent<DeadBody>().isDetected;
        if(!isDetected) {
            state = State.Chase;
            playerPosition =  _transform.position;
            _transform.GetComponentInParent<DeadBody>().isDetected = true;
            isDeadBody = true;
        }
    }

    private Vector3 RandomNavmeshLocation(float radius) {
        Vector3 finalPosition = Vector3.zero;
        Vector3 randomDirection = Random.insideUnitSphere * radius;
        randomDirection += transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, radius, 1)) {
            finalPosition = hit.position;     
        }
        return finalPosition;
    }

    private void HandleOnAlert(Vector3 pos) {
        isAlert = true;
        playerPosition = pos;
        state = State.Chase;
    }

    private void HandleOnAlertOff() {
        isDeadBody = false;
        isAlert = false;
        state = State.Idle;
    }

    
    private void HandlAnimation() {
        Vector3 horizontalVelocity = new Vector3(agent.velocity.x, 0, agent.velocity.z);
        float Velocity = horizontalVelocity.magnitude/enemy.speed;
        if(Velocity > 0) {
            animator.SetFloat(velocityHash, Velocity);
        } else {
            float v = animator.GetFloat(velocityHash);
            v = v> 0.01f ? Mathf.Lerp(v, 0, 20f * Time.deltaTime): 0;
            animator.SetFloat(velocityHash, v);
        }
    }

    private void OnDisable() {
        gameManager.OnEnemyAlert.RemoveListener(HandleOnAlert);
        gameManager.OnEnemyAlertOff.RemoveListener(HandleOnAlertOff);

        playerScanner.OnDetectedTarget.RemoveListener(HandleWhenDetected);
        playerScanner.OnNotDetectedTarget.RemoveListener(HandleWhenNotDetected);
        playerScanner.OnDetectedSubTarget.RemoveListener(HandleWhenDetectedSubtarget);
    }

    

#if UNITY_EDITOR
    private void OnDrawGizmosSelected() {
        playerScanner.EditorGizmo(rootScanner, enemy.detectionAngle, enemy.viewDistance);
    }
#endif
}
