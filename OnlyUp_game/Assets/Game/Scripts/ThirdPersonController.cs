using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInput))]
    public class ThirdPersonController : MonoBehaviour
    {
        public float MoveSpeed = 2.0f;

        public float SprintSpeed = 5.335f;

        public float RotationSmoothTime = 0.12f;

        public float SpeedChangeRate = 10.0f;

        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        public float FootstepAudioVolume = 0.5f;
        public AudioClip LoseAudioClip;
        public float LoseAudioVolume = 1.0f;
        public AudioSource LoseSfxSource;
        public GameObject LoseVfxPrefab;
        public Transform LoseVfxSpawnPoint;
        public float LoseVfxHeightOffset = 1.0f;
        public float LoseVfxLifetime = 2.0f;

        public float JumpHeight = 1.2f;

        public float Gravity = -15.0f;

        public float JumpTimeout = 0.50f;

        public float FallTimeout = 0.15f;

        public bool Grounded = true;

        public float GroundedOffset = -0.14f;

        public float GroundedRadius = 0.28f;

        public LayerMask GroundLayers;

        public GameObject CinemachineCameraTarget;

        public float TopClamp = 70.0f;

        public float BottomClamp = -30.0f;

        public float CameraAngleOverride = 0.0f;

        public bool LockCameraPosition = false;

        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        private float _speed;
        private float _animationBlend;
        private float _targetRotation;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private const float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;

        private PlayerInput _playerInput;
        private Animator _animator;
        private CharacterController _controller;
        private Renderer[] _characterRenderers;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;

        private const float _threshold = 0.01f;

        private bool _hasAnimator;

        public string lavaTag = "Lavafloor";
        public float respawnDelay = 1.25f;
        public Transform mainPlatformRespawnPoint;

        private Vector3 _spawnFallbackPosition;
        private Quaternion _spawnFallbackRotation;
        private bool _isRespawning;
        private string _overlayLoseMessage;
        private float _overlayHideTime;

        private bool IsCurrentDeviceMouse
        {
            get => _playerInput.currentControlScheme == "KeyboardMouse";
        }

        private void Awake()
        {
            _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
        }

        private void Start()
        {
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;
            _spawnFallbackPosition = transform.position;
            _spawnFallbackRotation = transform.rotation;
            
            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _characterRenderers = GetComponentsInChildren<Renderer>(true);
            _input = GetComponent<StarterAssetsInputs>();
            _playerInput = GetComponent<PlayerInput>();

            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");

            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
        }

        private void Update()
        {
            if (_isRespawning) return;

            JumpAndGravity();
            GroundedCheck();
            Move();
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
                transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
                QueryTriggerInteraction.Ignore);

            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                _cinemachineTargetYaw += _input.look.x * deltaTimeMultiplier;
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            _cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride,
                _cinemachineTargetYaw, 0.0f);
        }

        private void Move()
        {
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                    Time.deltaTime * SpeedChangeRate);

                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            if (_input.move != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                  _mainCamera.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                    RotationSmoothTime);

                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }


            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;

                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                _input.jump = false;
            }

            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (FootstepAudioClips.Length > 0)
                {
                    var index = Random.Range(0, FootstepAudioClips.Length);
                    AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
                }
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.gameObject.CompareTag(lavaTag)) HandleLose();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(lavaTag)) HandleLose();
        }

        private void HandleLose()
        {
            if (_isRespawning) return;

            SpawnLoseVfx();
            SetCharacterVisible(false);

            if (LoseAudioClip != null && LoseSfxSource != null)
            {
                LoseSfxSource.PlayOneShot(LoseAudioClip, LoseAudioVolume);
            }

            StartCoroutine(Respawn());
        }

        private void SpawnLoseVfx()
        {
            if (LoseVfxPrefab == null) return;

            Vector3 spawnPos = LoseVfxSpawnPoint != null ? LoseVfxSpawnPoint.position : transform.position;
            spawnPos.y += LoseVfxHeightOffset;
            Quaternion spawnRot = LoseVfxSpawnPoint != null ? LoseVfxSpawnPoint.rotation : Quaternion.identity;

            GameObject vfxInstance = Instantiate(LoseVfxPrefab, spawnPos, spawnRot);
            vfxInstance.SetActive(true);

            var particleSystems = vfxInstance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Clear(true);
                particleSystems[i].Play(true);
            }

            if (LoseVfxLifetime > 0.0f)
            {
                Destroy(vfxInstance, LoseVfxLifetime);
            }
        }

        private IEnumerator Respawn()
        {
            _isRespawning = true;
            _overlayLoseMessage = "You Lose!";
            _overlayHideTime = Time.time + Mathf.Max(respawnDelay + 0.5f, 1.0f);
            _controller.enabled = false;
            yield return new WaitForSeconds(respawnDelay);

            var pos = mainPlatformRespawnPoint != null ? mainPlatformRespawnPoint.position : _spawnFallbackPosition;
            var rot = mainPlatformRespawnPoint != null ? mainPlatformRespawnPoint.rotation : _spawnFallbackRotation;
            transform.SetPositionAndRotation(pos, rot);
            _verticalVelocity = 0.0f;
            _controller.enabled = true;
            SetCharacterVisible(true);
            _overlayLoseMessage = string.Empty;
            _isRespawning = false;
        }

        private void SetCharacterVisible(bool visible)
        {
            if (_characterRenderers == null) return;

            for (int i = 0; i < _characterRenderers.Length; i++)
            {
                if (_characterRenderers[i] != null)
                {
                    _characterRenderers[i].enabled = visible;
                }
            }
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_overlayLoseMessage) || Time.time > _overlayHideTime) return;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 44;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;

            Rect textRect = new Rect(0, Screen.height * 0.30f, Screen.width, 80f);
            GUI.Label(textRect, _overlayLoseMessage, style);
        }
    }
}