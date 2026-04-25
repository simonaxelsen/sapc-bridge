using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class GpuPrefabBurstSpawner : MonoBehaviour
{
    public enum BurstPlaybackMode
    {
        Auto,
        ForceGpuBaked,
        ForceCpuAnimated
    }

    internal const string DefaultComputeResource = "GpuPrefabBurstSimulation";
    internal const string DefaultShaderResource = "GpuPrefabBurstInstancedURP";

    [Header("Prefab Source")]
    [Tooltip("Prefab to burst when the gaze object breaks. Mesh-based prefabs use the GPU path; other prefabs fall back to CPU instantiation.")]
    public GameObject sourcePrefab;

    [Tooltip("Optional target the burst drifts toward. Leave empty for a free burst.")]
    public Transform target;

    [Tooltip("Optional camera override for the GPU draw call.")]
    public Camera renderCamera;

    [Header("Count")]
    [Min(1)] public int instanceCount = 1000;

    [Tooltip("Max real prefab instances used when GPU rendering is unavailable or unsupported.")]
    [Min(1)] public int cpuFallbackCount = 64;

    [Header("Animation")]
    [Tooltip("Use this to compare the regular animated prefab path against the GPU baked path directly in the inspector.")]
    public BurstPlaybackMode playbackMode = BurstPlaybackMode.Auto;

    [Tooltip("Force the CPU path when the source prefab has Animation/Animator so the prefab animation can play while scattering.")]
    public bool preferAnimatedCpuFallback = true;

    [Tooltip("Bake legacy Animation clips like 'Take 001' at runtime and play them on the GPU for high instance counts.")]
    public bool bakeLegacyAnimationForGpu = true;

    [Tooltip("Safety cap for real animated prefab instances when animation playback forces the CPU path.")]
    [Min(1)] public int animatedCpuFallbackCount = 256;

    [Tooltip("Legacy Animation clip to play on spawned CPU fallback instances. Leave empty to use the prefab's default autoplay clip.")]
    public string animationClipName = "Take 001";

    [Min(1f)] public float animationBakeFrameRate = 24f;
    [Min(0.01f)] public float animationPlaybackSpeed = 1f;
    [Min(0f)] public float animationPhaseJitter = 0.17f;
    [Range(0f, 1f)] public float cpuAnimationStartSpread = 1f;
    [Range(0f, 1f)] public float cpuAnimationSpeedJitter = 0.2f;

    [Header("Lifetime")]
    [Min(0.1f)] public float effectLifetime = 8f;

    [Tooltip("Use the destroyed object's rotation for the burst root.")]
    public bool inheritSourceRotation = true;

    [Header("Burst Shape")]
    [Min(0f)] public float spawnRadius = 0.5f;

    [Header("Motion")]
    [Min(0f)] public float minSpeed = 1.5f;
    [Min(0f)] public float maxSpeed = 3.5f;
    [Min(0f)] public float upwardBias = 1.25f;
    [Min(0f)] public float burstAcceleration = 7f;
    [Min(0.01f)] public float burstDecay = 1.75f;
    [Min(0f)] public float velocityDamping = 0.2f;
    [Min(0f)] public float flutterAmplitude = 0.75f;
    [Min(0f)] public float flutterFrequency = 2.0f;
    [Min(0f)] public float chaosStrength = 1.5f;
    [Min(0f)] public float chaosFrequency = 4.5f;
    [Min(0f)] public float separationRadius = 1.75f;
    [Min(0f)] public float separationStrength = 6f;
    [Min(0f)] public float attractionStrength = 0f;
    [Min(0f)] public float maxAttractionSpeed = 5f;
    [Range(0f, 2f)] public float cpuScatterDirectionJitter = 0.7f;
    [Range(0f, 1f)] public float cpuMotionJitter = 0.35f;

    [Header("Scale")]
    [Min(0f)] public float minScale = 0.85f;
    [Min(0f)] public float maxScale = 1.15f;

    [Header("GPU Assets")]
    [Tooltip("Optional. If left empty the component loads Assets/Resources/GpuPrefabBurstSimulation.compute.")]
    public ComputeShader simulationShader;

    [Tooltip("Optional. If left empty the component loads Assets/Resources/GpuPrefabBurstInstancedURP.shader.")]
    public Shader instancedShader;

    public void Play()
    {
        Quaternion rotation = inheritSourceRotation ? transform.rotation : Quaternion.identity;
        Play(transform.position, rotation);
    }

    public void Play(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (sourcePrefab == null)
        {
            Debug.LogWarning("[GpuPrefabBurstSpawner] No sourcePrefab assigned.", this);
            return;
        }

        Quaternion burstRotation = inheritSourceRotation ? worldRotation : Quaternion.identity;

        if (ShouldUseGpuBurst())
        {
            SpawnGpuBurst(worldPosition, burstRotation);
            return;
        }

        SpawnCpuFallback(worldPosition, burstRotation);
    }

    private void OnValidate()
    {
        instanceCount = Mathf.Max(1, instanceCount);
        cpuFallbackCount = Mathf.Max(1, cpuFallbackCount);
        animatedCpuFallbackCount = Mathf.Max(1, animatedCpuFallbackCount);
        animationBakeFrameRate = Mathf.Max(1f, animationBakeFrameRate);
        animationPlaybackSpeed = Mathf.Max(0.01f, animationPlaybackSpeed);
        maxSpeed = Mathf.Max(minSpeed, maxSpeed);
        burstDecay = Mathf.Max(0.01f, burstDecay);
        separationRadius = Mathf.Max(0f, separationRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
        maxScale = Mathf.Max(minScale, maxScale);
        maxAttractionSpeed = Mathf.Max(maxSpeed, maxAttractionSpeed);
    }

    private bool ShouldUseGpuBurst()
    {
        if (playbackMode == BurstPlaybackMode.ForceCpuAnimated)
        {
            return false;
        }

        bool hasPlayableAnimation = HasPlayableAnimation(sourcePrefab);
        bool canBakeLegacyAnimation = bakeLegacyAnimationForGpu && ResolveLegacyAnimationClip(sourcePrefab, animationClipName) != null;
        bool autoRejectForAnimation = playbackMode == BurstPlaybackMode.Auto
            && preferAnimatedCpuFallback
            && hasPlayableAnimation
            && !canBakeLegacyAnimation;

        return SystemInfo.supportsComputeShaders
            && SystemInfo.supportsInstancing
            && !HasSkinnedMeshes(sourcePrefab)
            && !autoRejectForAnimation
            && (HasBurstSimulationKernels(simulationShader)
                || HasBurstSimulationKernels(Resources.Load<ComputeShader>(DefaultComputeResource)))
            && (simulationShader != null || Resources.Load<ComputeShader>(DefaultComputeResource) != null)
            && (instancedShader != null || Resources.Load<Shader>(DefaultShaderResource) != null)
            && HasMeshRenderers(sourcePrefab);
    }

    private void SpawnGpuBurst(Vector3 worldPosition, Quaternion worldRotation)
    {
        GameObject runtimeHost = new GameObject($"GpuPrefabBurst_{sourcePrefab.name}");
        runtimeHost.layer = gameObject.layer;
        runtimeHost.transform.SetPositionAndRotation(worldPosition, worldRotation);

        GpuPrefabBurstRuntime runtime = runtimeHost.AddComponent<GpuPrefabBurstRuntime>();
        runtime.Initialize(this);
    }

    private void SpawnCpuFallback(Vector3 worldPosition, Quaternion worldRotation)
    {
        bool animatedFallback = playbackMode == BurstPlaybackMode.ForceCpuAnimated
            || (preferAnimatedCpuFallback && HasPlayableAnimation(sourcePrefab));
        int count = animatedFallback
            ? Mathf.Min(instanceCount, animatedCpuFallbackCount)
            : Mathf.Min(instanceCount, cpuFallbackCount);

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = Random.insideUnitSphere * spawnRadius;
            offset.y = Mathf.Abs(offset.y) * upwardBias;
            Vector3 baseBurstDirection = offset.sqrMagnitude > 0.0001f
                ? offset.normalized
                : RandomBurstDirection();
            Vector3 burstDirection = (baseBurstDirection + Random.insideUnitSphere * cpuScatterDirectionJitter).normalized;
            if (burstDirection.sqrMagnitude < 0.0001f)
            {
                burstDirection = baseBurstDirection;
            }

            float motionVariation = Random.Range(Mathf.Max(0.25f, 1f - cpuMotionJitter), 1f + cpuMotionJitter);
            float animationStartOffset = Random.value * cpuAnimationStartSpread;
            float animationSpeed = animationPlaybackSpeed * Random.Range(Mathf.Max(0.25f, 1f - cpuAnimationSpeedJitter), 1f + cpuAnimationSpeedJitter);

            Quaternion rotation = worldRotation * Random.rotation;
            GameObject runtimeRoot = new GameObject($"{sourcePrefab.name}_BurstRoot");
            runtimeRoot.layer = gameObject.layer;
            runtimeRoot.transform.SetPositionAndRotation(worldPosition + offset, rotation);

            GameObject instance = Instantiate(sourcePrefab, runtimeRoot.transform);
            instance.transform.localPosition = sourcePrefab.transform.localPosition;
            instance.transform.localRotation = sourcePrefab.transform.localRotation;

            float scale = Random.Range(minScale, maxScale);
            instance.transform.localScale = sourcePrefab.transform.localScale * scale;

            PlaySourceAnimation(instance, animationStartOffset, animationSpeed);

            CpuBurstDrift drift = runtimeRoot.AddComponent<CpuBurstDrift>();
            drift.Initialize(
                burstDirection * Random.Range(minSpeed, maxSpeed),
                burstDirection,
                worldPosition,
                worldRotation,
                target,
                burstAcceleration * motionVariation,
                burstDecay,
                velocityDamping,
                flutterAmplitude * motionVariation,
                flutterFrequency * Random.Range(Mathf.Max(0.25f, 1f - cpuMotionJitter), 1f + cpuMotionJitter),
                chaosStrength * motionVariation,
                chaosFrequency * Random.Range(Mathf.Max(0.25f, 1f - cpuMotionJitter), 1f + cpuMotionJitter),
                separationRadius,
                separationStrength * motionVariation,
                attractionStrength,
                maxAttractionSpeed,
                effectLifetime);
        }

        string fallbackReason = playbackMode == BurstPlaybackMode.ForceCpuAnimated
            ? "forced CPU comparison mode"
            : (playbackMode == BurstPlaybackMode.ForceGpuBaked
                ? "forced GPU mode was unavailable"
                : (animatedFallback ? "animation playback" : "GPU fallback"));
        string countSuffix = count < instanceCount ? $" (capped from {instanceCount})" : string.Empty;
        Debug.LogWarning(
            $"[GpuPrefabBurstSpawner] Using CPU fallback for '{sourcePrefab.name}' due to {fallbackReason}. Spawned {count} real prefab instances{countSuffix}.",
            this);
    }

    internal void SpawnCpuFallbackFromRuntime(Vector3 worldPosition, Quaternion worldRotation)
    {
        SpawnCpuFallback(worldPosition, worldRotation);
    }

    private Vector3 RandomBurstDirection()
    {
        Vector3 direction = Random.onUnitSphere;
        direction.y = Mathf.Abs(direction.y) + upwardBias;
        direction.Normalize();

        return direction;
    }

    private static bool HasMeshRenderers(GameObject prefab)
    {
        if (prefab == null)
        {
            return false;
        }

        MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterials.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasSkinnedMeshes(GameObject prefab)
    {
        return prefab != null && prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
    }

    private static bool HasBurstSimulationKernels(ComputeShader shader)
    {
        if (shader == null)
        {
            return false;
        }

        try
        {
            return shader.FindKernel("InitializeBurst") >= 0 && shader.FindKernel("UpdateBurst") >= 0;
        }
        catch (System.ArgumentException)
        {
            return false;
        }
    }

    internal static AnimationClip ResolveLegacyAnimationClip(GameObject prefab, string clipName)
    {
        if (prefab == null)
        {
            return null;
        }

        Animation legacyAnimation = prefab.GetComponentInChildren<Animation>(true);
        if (legacyAnimation == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(clipName))
        {
            AnimationClip namedClip = legacyAnimation.GetClip(clipName);
            if (namedClip != null)
            {
                return namedClip;
            }
        }

        return legacyAnimation.clip;
    }

    private bool HasPlayableAnimation(GameObject prefab)
    {
        if (prefab == null)
        {
            return false;
        }

        if (ResolveLegacyAnimationClip(prefab, animationClipName) != null)
        {
            return true;
        }

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        return animator != null && animator.runtimeAnimatorController != null;
    }

    private void PlaySourceAnimation(GameObject instance, float normalizedStartOffset, float playbackSpeed)
    {
        if (instance == null)
        {
            return;
        }

        Animation legacyAnimation = instance.GetComponentInChildren<Animation>(true);
        if (legacyAnimation != null)
        {
            legacyAnimation.enabled = true;
            string clipToPlay = null;

            if (!string.IsNullOrWhiteSpace(animationClipName) && legacyAnimation.GetClip(animationClipName) != null)
            {
                clipToPlay = animationClipName;
            }
            else if (legacyAnimation.clip != null)
            {
                clipToPlay = legacyAnimation.clip.name;
            }

            if (string.IsNullOrWhiteSpace(clipToPlay))
            {
                return;
            }

            legacyAnimation.Play(clipToPlay);
            AnimationState state = legacyAnimation[clipToPlay];
            if (state != null)
            {
                state.speed = playbackSpeed;
                state.time = state.length * Mathf.Repeat(normalizedStartOffset, 1f);
                legacyAnimation.Sample();
            }

            return;
        }

        Animator animator = instance.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.enabled = true;
            animator.Rebind();
            float clipLength = animator.runtimeAnimatorController.animationClips.Length > 0
                ? animator.runtimeAnimatorController.animationClips[0].length
                : 1f;
            animator.Update(Mathf.Repeat(normalizedStartOffset, 1f) * clipLength);
            animator.speed = playbackSpeed;
        }
    }
}

internal sealed class GpuPrefabBurstRuntime : MonoBehaviour
{
    private const int ThreadsPerGroup = 64;
    private const int StateStride = sizeof(float) * 12;
    private const int MatrixStride = sizeof(float) * 16;
    private static readonly int StatesId = Shader.PropertyToID("_States");
    private static readonly int AnimationMatricesId = Shader.PropertyToID("_AnimationMatrices");
    private static readonly int InstanceCountId = Shader.PropertyToID("_InstanceCount");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int TimeId = Shader.PropertyToID("_Time");
    private static readonly int SpawnRadiusId = Shader.PropertyToID("_SpawnRadius");
    private static readonly int MinSpeedId = Shader.PropertyToID("_MinSpeed");
    private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
    private static readonly int UpwardBiasId = Shader.PropertyToID("_UpwardBias");
    private static readonly int BurstAccelerationId = Shader.PropertyToID("_BurstAcceleration");
    private static readonly int BurstDecayId = Shader.PropertyToID("_BurstDecay");
    private static readonly int VelocityDampingId = Shader.PropertyToID("_VelocityDamping");
    private static readonly int MinScaleId = Shader.PropertyToID("_MinScale");
    private static readonly int MaxScaleId = Shader.PropertyToID("_MaxScale");
    private static readonly int FlutterAmplitudeId = Shader.PropertyToID("_FlutterAmplitude");
    private static readonly int FlutterFrequencyId = Shader.PropertyToID("_FlutterFrequency");
    private static readonly int ChaosStrengthId = Shader.PropertyToID("_ChaosStrength");
    private static readonly int ChaosFrequencyId = Shader.PropertyToID("_ChaosFrequency");
    private static readonly int SeparationRadiusId = Shader.PropertyToID("_SeparationRadius");
    private static readonly int SeparationStrengthId = Shader.PropertyToID("_SeparationStrength");
    private static readonly int HasTargetId = Shader.PropertyToID("_HasTarget");
    private static readonly int TargetPositionId = Shader.PropertyToID("_TargetPosition");
    private static readonly int AttractionStrengthId = Shader.PropertyToID("_AttractionStrength");
    private static readonly int MaxAttractionSpeedId = Shader.PropertyToID("_MaxAttractionSpeed");
    private static readonly int RootMatrixId = Shader.PropertyToID("_RootMatrix");
    private static readonly int PartMatrixId = Shader.PropertyToID("_PartMatrix");
    private static readonly int UseBakedAnimationId = Shader.PropertyToID("_UseBakedAnimation");
    private static readonly int AnimationPartIndexId = Shader.PropertyToID("_AnimationPartIndex");
    private static readonly int AnimationPartCountId = Shader.PropertyToID("_AnimationPartCount");
    private static readonly int AnimationFrameCountId = Shader.PropertyToID("_AnimationFrameCount");
    private static readonly int AnimationFrameFloatId = Shader.PropertyToID("_AnimationFrameFloat");
    private static readonly int AnimationPhaseJitterId = Shader.PropertyToID("_AnimationPhaseJitter");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int OpaqueTextureId = Shader.PropertyToID("_OpaqueTexture");

    private readonly List<RenderPart> _renderParts = new List<RenderPart>();
    private ComputeShader _simulationShader;
    private Shader _instancedShader;
    private ComputeBuffer _stateBuffer;
    private ComputeBuffer _animationMatrixBuffer;
    private MaterialPropertyBlock _propertyBlock;
    private Transform _target;
    private Camera _renderCamera;
    private GameObject _sourcePrefab;
    private GpuPrefabBurstSpawner _owner;
    private Bounds _drawBounds;
    private float _maxTravelExtent;
    private float _elapsed;
    private float _effectLifetime;
    private float _spawnRadius;
    private float _minSpeed;
    private float _maxSpeed;
    private float _upwardBias;
    private float _burstAcceleration;
    private float _burstDecay;
    private float _velocityDamping;
    private float _minScale;
    private float _maxScale;
    private float _flutterAmplitude;
    private float _flutterFrequency;
    private float _chaosStrength;
    private float _chaosFrequency;
    private float _separationRadius;
    private float _separationStrength;
    private float _attractionStrength;
    private float _maxAttractionSpeed;
    private float _animationBakeFrameRate;
    private float _animationPlaybackSpeed;
    private float _animationPhaseJitter;
    private int _instanceCount;
    private int _dispatchGroupCount;
    private int _layer;
    private int _initializeKernel;
    private int _updateKernel;
    private int _animationFrameCount;
    private float _animationDuration;
    private string _animationClipName;
    private bool _bakeLegacyAnimationForGpu;
    private bool _useBakedAnimation;
    private bool _initialized;
    private bool _loggedInitializationFailure;

    public void Initialize(GpuPrefabBurstSpawner source)
    {
        _owner = source;
        _sourcePrefab = source.sourcePrefab;
        _target = source.target;
        _renderCamera = source.renderCamera;
        _effectLifetime = source.effectLifetime;
        _spawnRadius = source.spawnRadius;
        _minSpeed = source.minSpeed;
        _maxSpeed = source.maxSpeed;
        _upwardBias = source.upwardBias;
        _burstAcceleration = source.burstAcceleration;
        _burstDecay = source.burstDecay;
        _velocityDamping = source.velocityDamping;
        _minScale = source.minScale;
        _maxScale = source.maxScale;
        _flutterAmplitude = source.flutterAmplitude;
        _flutterFrequency = source.flutterFrequency;
        _chaosStrength = source.chaosStrength;
        _chaosFrequency = source.chaosFrequency;
        _separationRadius = source.separationRadius;
        _separationStrength = source.separationStrength;
        _attractionStrength = source.attractionStrength;
        _maxAttractionSpeed = source.maxAttractionSpeed;
        _animationBakeFrameRate = source.animationBakeFrameRate;
        _animationPlaybackSpeed = source.animationPlaybackSpeed;
        _animationPhaseJitter = source.animationPhaseJitter;
        _instanceCount = source.instanceCount;
        _layer = source.gameObject.layer;
        _animationClipName = source.animationClipName;
        _bakeLegacyAnimationForGpu = source.bakeLegacyAnimationForGpu;
        _simulationShader = source.simulationShader != null
            ? source.simulationShader
            : Resources.Load<ComputeShader>(GpuPrefabBurstSpawner.DefaultComputeResource);
        _instancedShader = source.instancedShader != null
            ? source.instancedShader
            : Resources.Load<Shader>(GpuPrefabBurstSpawner.DefaultShaderResource);
    }

    private void Update()
    {
        if (!_initialized && !TryInitialize())
        {
            if (_owner != null)
            {
                _owner.SpawnCpuFallbackFromRuntime(transform.position, transform.rotation);
            }

            Destroy(gameObject);
            return;
        }

        _elapsed += Time.deltaTime;
        if (_elapsed >= _effectLifetime)
        {
            Destroy(gameObject);
            return;
        }

        SetSimulationParameters();
        _simulationShader.Dispatch(_updateKernel, _dispatchGroupCount, 1, 1);
        UpdateDrawBounds();

        Matrix4x4 rootMatrix = transform.localToWorldMatrix;
        _propertyBlock.SetBuffer(StatesId, _stateBuffer);
        _propertyBlock.SetMatrix(RootMatrixId, rootMatrix);
        _propertyBlock.SetFloat(UseBakedAnimationId, _useBakedAnimation ? 1f : 0f);
        _propertyBlock.SetInt(AnimationPartCountId, _renderParts.Count);
        _propertyBlock.SetInt(AnimationFrameCountId, _animationFrameCount);
        _propertyBlock.SetFloat(AnimationPhaseJitterId, _animationPhaseJitter);

        if (_useBakedAnimation)
        {
            float normalizedFrame = _animationDuration > 0.0001f
                ? (_elapsed * _animationPlaybackSpeed / _animationDuration)
                : 0f;
            _propertyBlock.SetBuffer(AnimationMatricesId, _animationMatrixBuffer);
            _propertyBlock.SetFloat(AnimationFrameFloatId, normalizedFrame);
        }
        else
        {
            _propertyBlock.SetFloat(AnimationFrameFloatId, 0f);
        }

        foreach (RenderPart part in _renderParts)
        {
            _propertyBlock.SetMatrix(PartMatrixId, part.LocalMatrix);
            _propertyBlock.SetInt(AnimationPartIndexId, part.AnimationPartIndex);
            Graphics.DrawMeshInstancedProcedural(
                part.Mesh,
                part.SubMeshIndex,
                part.Material,
                _drawBounds,
                _instanceCount,
                _propertyBlock,
                ShadowCastingMode.Off,
                false,
                _layer,
                _renderCamera);
        }
    }

    private void OnDestroy()
    {
        if (_stateBuffer != null)
        {
            _stateBuffer.Release();
            _stateBuffer = null;
        }

        if (_animationMatrixBuffer != null)
        {
            _animationMatrixBuffer.Release();
            _animationMatrixBuffer = null;
        }

        foreach (RenderPart part in _renderParts)
        {
            if (part.Material != null)
            {
                Destroy(part.Material);
            }
        }

        _renderParts.Clear();
    }

    private bool TryInitialize()
    {
        if (_sourcePrefab == null || _simulationShader == null || _instancedShader == null)
        {
            return false;
        }

        if (!BuildRenderParts())
        {
            Debug.LogWarning("[GpuPrefabBurstRuntime] The source prefab has no mesh parts that can be drawn on the GPU.", this);
            return false;
        }

        BakeLegacyAnimationIfAvailable();

        if (!TryResolveSimulationKernels())
        {
            return false;
        }

        _propertyBlock = new MaterialPropertyBlock();
        _stateBuffer = new ComputeBuffer(_instanceCount, StateStride);
        _dispatchGroupCount = Mathf.CeilToInt(_instanceCount / (float)ThreadsPerGroup);
        _simulationShader.SetBuffer(_initializeKernel, StatesId, _stateBuffer);
        _simulationShader.SetBuffer(_updateKernel, StatesId, _stateBuffer);

        float maxTravel = _effectLifetime * Mathf.Max(
            _maxSpeed + _burstAcceleration,
            _maxAttractionSpeed + _flutterAmplitude + _maxSpeed + _burstAcceleration);
        _maxTravelExtent = Mathf.Max(2f, _spawnRadius + maxTravel);
        UpdateDrawBounds();

        SetSimulationParameters();
        _simulationShader.Dispatch(_initializeKernel, _dispatchGroupCount, 1, 1);
        _initialized = true;
        return true;
    }

    private bool TryResolveSimulationKernels()
    {
        if (TryFindSimulationKernels(_simulationShader, out int initializeKernel, out int updateKernel))
        {
            _initializeKernel = initializeKernel;
            _updateKernel = updateKernel;
            return true;
        }

        ComputeShader fallbackShader = Resources.Load<ComputeShader>(GpuPrefabBurstSpawner.DefaultComputeResource);
        if (fallbackShader != null
            && fallbackShader != _simulationShader
            && TryFindSimulationKernels(fallbackShader, out initializeKernel, out updateKernel))
        {
            _simulationShader = fallbackShader;
            _initializeKernel = initializeKernel;
            _updateKernel = updateKernel;
            return true;
        }

        if (!_loggedInitializationFailure)
        {
            string shaderName = _simulationShader != null ? _simulationShader.name : "null";
            Debug.LogWarning(
                $"[GpuPrefabBurstRuntime] Compute shader '{shaderName}' is missing InitializeBurst/UpdateBurst. Assign GpuPrefabBurstSimulation.compute or reimport it. Falling back by disabling this GPU burst.",
                this);
            _loggedInitializationFailure = true;
        }

        return false;
    }

    private static bool TryFindSimulationKernels(ComputeShader shader, out int initializeKernel, out int updateKernel)
    {
        initializeKernel = -1;
        updateKernel = -1;

        if (shader == null)
        {
            return false;
        }

        try
        {
            initializeKernel = shader.FindKernel("InitializeBurst");
            updateKernel = shader.FindKernel("UpdateBurst");
            return initializeKernel >= 0 && updateKernel >= 0;
        }
        catch (System.ArgumentException)
        {
            return false;
        }
    }

    private void SetSimulationParameters()
    {
        Vector3 targetPosition = _target != null ? transform.InverseTransformPoint(_target.position) : Vector3.zero;

        _simulationShader.SetInt(InstanceCountId, _instanceCount);
        _simulationShader.SetFloat(DeltaTimeId, Time.deltaTime);
        _simulationShader.SetFloat(TimeId, Time.time);
        _simulationShader.SetFloat(SpawnRadiusId, _spawnRadius);
        _simulationShader.SetFloat(MinSpeedId, _minSpeed);
        _simulationShader.SetFloat(MaxSpeedId, _maxSpeed);
        _simulationShader.SetFloat(UpwardBiasId, _upwardBias);
        _simulationShader.SetFloat(BurstAccelerationId, _burstAcceleration);
        _simulationShader.SetFloat(BurstDecayId, _burstDecay);
        _simulationShader.SetFloat(VelocityDampingId, _velocityDamping);
        _simulationShader.SetFloat(MinScaleId, _minScale);
        _simulationShader.SetFloat(MaxScaleId, _maxScale);
        _simulationShader.SetFloat(FlutterAmplitudeId, _flutterAmplitude);
        _simulationShader.SetFloat(FlutterFrequencyId, _flutterFrequency);
        _simulationShader.SetFloat(ChaosStrengthId, _chaosStrength);
        _simulationShader.SetFloat(ChaosFrequencyId, _chaosFrequency);
        _simulationShader.SetFloat(SeparationRadiusId, _separationRadius);
        _simulationShader.SetFloat(SeparationStrengthId, _separationStrength);
        _simulationShader.SetInt(HasTargetId, _target != null ? 1 : 0);
        _simulationShader.SetVector(TargetPositionId, targetPosition);
        _simulationShader.SetFloat(AttractionStrengthId, _attractionStrength);
        _simulationShader.SetFloat(MaxAttractionSpeedId, _maxAttractionSpeed);
    }

    private void UpdateDrawBounds()
    {
        Vector3 origin = transform.position;
        Vector3 focus = _target != null ? _target.position : origin;
        Vector3 min = Vector3.Min(origin, focus) - Vector3.one * _maxTravelExtent;
        Vector3 max = Vector3.Max(origin, focus) + Vector3.one * _maxTravelExtent;
        _drawBounds.SetMinMax(min, max);
    }

    private bool BuildRenderParts()
    {
        MeshFilter[] meshFilters = _sourcePrefab.GetComponentsInChildren<MeshFilter>(true);
        Matrix4x4 rootInverse = _sourcePrefab.transform.worldToLocalMatrix;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                continue;
            }

            Material[] sourceMaterials = meshRenderer.sharedMaterials;
            int subMeshCount = Mathf.Min(meshFilter.sharedMesh.subMeshCount, sourceMaterials.Length);
            Matrix4x4 localMatrix = rootInverse * meshFilter.transform.localToWorldMatrix;
            string transformPath = GetTransformPath(_sourcePrefab.transform, meshFilter.transform);

            for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                Material sourceMaterial = sourceMaterials[subMeshIndex];
                if (sourceMaterial == null)
                {
                    continue;
                }

                Material runtimeMaterial = new Material(_instancedShader)
                {
                    enableInstancing = true,
                    hideFlags = HideFlags.DontSave,
                    name = $"GpuBurst_{sourceMaterial.name}"
                };

                CopyMaterialProperties(sourceMaterial, runtimeMaterial);
                _renderParts.Add(new RenderPart(
                    meshFilter.sharedMesh,
                    subMeshIndex,
                    runtimeMaterial,
                    localMatrix,
                    _renderParts.Count,
                    transformPath));
            }
        }

        return _renderParts.Count > 0;
    }

    private void BakeLegacyAnimationIfAvailable()
    {
        _useBakedAnimation = false;
        _animationFrameCount = 0;
        _animationDuration = 0f;

        if (!_bakeLegacyAnimationForGpu)
        {
            return;
        }

        if (GpuPrefabBurstSpawner.HasSkinnedMeshes(_sourcePrefab))
        {
            return;
        }

        AnimationClip clip = GpuPrefabBurstSpawner.ResolveLegacyAnimationClip(_sourcePrefab, _animationClipName);
        if (clip == null)
        {
            return;
        }

        GameObject sampleRoot = Instantiate(_sourcePrefab);
        sampleRoot.hideFlags = HideFlags.HideAndDontSave;

        foreach (Renderer renderer in sampleRoot.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }

        foreach (Animation animation in sampleRoot.GetComponentsInChildren<Animation>(true))
        {
            animation.enabled = false;
        }

        foreach (Animator animator in sampleRoot.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
        }

        Dictionary<string, Transform> pathMap = BuildTransformPathMap(sampleRoot.transform);
        int frameCount = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(clip.length, 0.01f) * _animationBakeFrameRate));
        Matrix4x4[] matrices = new Matrix4x4[frameCount * _renderParts.Count];

        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            float normalizedTime = frameIndex / (float)frameCount;
            clip.SampleAnimation(sampleRoot, normalizedTime * clip.length);
            Matrix4x4 rootInverse = sampleRoot.transform.worldToLocalMatrix;

            foreach (RenderPart part in _renderParts)
            {
                Matrix4x4 partMatrix = part.LocalMatrix;
                if (pathMap.TryGetValue(part.TransformPath, out Transform animatedTransform))
                {
                    partMatrix = rootInverse * animatedTransform.localToWorldMatrix;
                }

                matrices[frameIndex * _renderParts.Count + part.AnimationPartIndex] = partMatrix;
            }
        }

        Destroy(sampleRoot);

        _animationMatrixBuffer = new ComputeBuffer(matrices.Length, MatrixStride);
        _animationMatrixBuffer.SetData(matrices);
        _animationFrameCount = frameCount;
        _animationDuration = Mathf.Max(clip.length, 0.01f);
        _useBakedAnimation = true;
    }

    private static Dictionary<string, Transform> BuildTransformPathMap(Transform root)
    {
        Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            map[GetTransformPath(root, child)] = child;
        }

        return map;
    }

    private static string GetTransformPath(Transform root, Transform current)
    {
        if (current == root)
        {
            return string.Empty;
        }

        List<string> names = new List<string>();
        Transform walker = current;
        while (walker != null && walker != root)
        {
            names.Add(walker.name);
            walker = walker.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static void CopyMaterialProperties(Material source, Material destination)
    {
        Texture albedoTexture = null;
        Vector2 textureScale = Vector2.one;
        Vector2 textureOffset = Vector2.zero;
        if (source.HasProperty(BaseMapId))
        {
            albedoTexture = source.GetTexture(BaseMapId);
            textureScale = source.GetTextureScale(BaseMapId);
            textureOffset = source.GetTextureOffset(BaseMapId);
        }
        else if (source.HasProperty(MainTexId))
        {
            albedoTexture = source.GetTexture(MainTexId);
            textureScale = source.GetTextureScale(MainTexId);
            textureOffset = source.GetTextureOffset(MainTexId);
        }

        if (albedoTexture != null)
        {
            destination.SetTexture(BaseMapId, albedoTexture);
            destination.SetTextureScale(BaseMapId, textureScale);
            destination.SetTextureOffset(BaseMapId, textureOffset);
        }

        if (source.HasProperty(BaseColorId))
        {
            destination.SetColor(BaseColorId, source.GetColor(BaseColorId));
        }
        else if (source.HasProperty(ColorId))
        {
            destination.SetColor(BaseColorId, source.GetColor(ColorId));
        }

        float cutoff = source.HasProperty(CutoffId) ? source.GetFloat(CutoffId) : 0.5f;
        destination.SetFloat(CutoffId, cutoff);

        bool alphaClip = false;
        if (source.HasProperty("_AlphaClip"))
        {
            alphaClip = source.GetFloat("_AlphaClip") > 0.5f;
        }
        else if (source.HasProperty("_Mode"))
        {
            alphaClip = Mathf.Approximately(source.GetFloat("_Mode"), 1f);
        }

        bool transparent = source.renderQueue >= (int)RenderQueue.Transparent;
        if (source.HasProperty("_Surface"))
        {
            transparent |= source.GetFloat("_Surface") > 0.5f;
        }

        if (source.HasProperty("_Mode"))
        {
            float mode = source.GetFloat("_Mode");
            transparent |= mode >= 2f;
        }

        destination.SetFloat(SrcBlendId, transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
        destination.SetFloat(DstBlendId, transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
        destination.SetFloat(ZWriteId, transparent ? 0f : 1f);
        destination.SetFloat(CullId, source.HasProperty(CullId) ? source.GetFloat(CullId) : 2f);
        destination.SetFloat(OpaqueTextureId, alphaClip ? 1f : 0f);
        destination.SetOverrideTag("RenderType", transparent ? "Transparent" : (alphaClip ? "TransparentCutout" : "Opaque"));
        destination.renderQueue = transparent
            ? (int)RenderQueue.Transparent
            : (alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry);
    }

    private readonly struct RenderPart
    {
        public readonly Mesh Mesh;
        public readonly int SubMeshIndex;
        public readonly Material Material;
        public readonly Matrix4x4 LocalMatrix;
        public readonly int AnimationPartIndex;
        public readonly string TransformPath;

        public RenderPart(Mesh mesh, int subMeshIndex, Material material, Matrix4x4 localMatrix, int animationPartIndex, string transformPath)
        {
            Mesh = mesh;
            SubMeshIndex = subMeshIndex;
            Material = material;
            LocalMatrix = localMatrix;
            AnimationPartIndex = animationPartIndex;
            TransformPath = transformPath;
        }
    }
}

internal sealed class CpuBurstDrift : MonoBehaviour
{
    private static readonly List<CpuBurstDrift> ActiveDrifts = new List<CpuBurstDrift>();

    private Transform _target;
    private Vector3 _velocity;
    private Vector3 _burstDirection;
    private Vector3 _rootPosition;
    private Quaternion _rootRotation;
    private float _burstAcceleration;
    private float _burstDecay;
    private float _velocityDamping;
    private float _flutterAmplitude;
    private float _flutterFrequency;
    private float _chaosStrength;
    private float _chaosFrequency;
    private float _separationRadius;
    private float _separationStrength;
    private float _attractionStrength;
    private float _maxAttractionSpeed;
    private float _lifetime;
    private float _seed;
    private float _elapsed;

    public void Initialize(
        Vector3 initialVelocity,
        Vector3 burstDirection,
        Vector3 rootPosition,
        Quaternion rootRotation,
        Transform target,
        float burstAcceleration,
        float burstDecay,
        float velocityDamping,
        float flutterAmplitude,
        float flutterFrequency,
        float chaosStrength,
        float chaosFrequency,
        float separationRadius,
        float separationStrength,
        float attractionStrength,
        float maxAttractionSpeed,
        float lifetime)
    {
        _velocity = initialVelocity;
        _burstDirection = burstDirection.sqrMagnitude > 0.0001f ? burstDirection.normalized : Vector3.up;
        _rootPosition = rootPosition;
        _rootRotation = rootRotation;
        _target = target;
        _burstAcceleration = burstAcceleration;
        _burstDecay = Mathf.Max(0.01f, burstDecay);
        _velocityDamping = velocityDamping;
        _flutterAmplitude = flutterAmplitude;
        _flutterFrequency = flutterFrequency;
        _chaosStrength = chaosStrength;
        _chaosFrequency = chaosFrequency;
        _separationRadius = separationRadius;
        _separationStrength = separationStrength;
        _attractionStrength = attractionStrength;
        _maxAttractionSpeed = maxAttractionSpeed;
        _lifetime = lifetime;
        _seed = Random.value * 1000f;
    }

    private void OnEnable()
    {
        ActiveDrifts.Add(this);
    }

    private void OnDisable()
    {
        ActiveDrifts.Remove(this);
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= _lifetime)
        {
            Destroy(gameObject);
            return;
        }

        if (_target != null)
        {
            Vector3 toTarget = _target.position - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                float attractionBlend = 1f - Mathf.Exp(-_elapsed * _burstDecay);
                _velocity += toTarget.normalized * (_attractionStrength * attractionBlend * Time.deltaTime);
            }
        }

        float burstWeight = Mathf.Exp(-_elapsed * _burstDecay);
        Vector3 localPosition = Quaternion.Inverse(_rootRotation) * (transform.position - _rootPosition);
        Vector3 radialDirection = localPosition.sqrMagnitude > 0.0001f ? localPosition.normalized : _burstDirection;
        Vector3 headingDirection = _velocity.sqrMagnitude > 0.0001f ? _velocity.normalized : radialDirection;
        _velocity += radialDirection * (_burstAcceleration * (0.7f + burstWeight) * Time.deltaTime);
        _velocity += headingDirection * (_flutterAmplitude * 0.18f * Time.deltaTime);
        _velocity += SampleEscapeScatter(_burstDirection, _velocity, _seed, _elapsed, _flutterFrequency, _chaosFrequency)
            * ((_flutterAmplitude * 0.22f + _chaosStrength * (0.08f + burstWeight * 0.25f)) * Time.deltaTime);
        _velocity += ComputeSeparationForce() * (_separationStrength * Time.deltaTime);
        _velocity /= 1f + _velocityDamping * Time.deltaTime;

        float speed = _velocity.magnitude;
        if (speed > _maxAttractionSpeed && speed > 0.0001f)
        {
            _velocity = _velocity / speed * _maxAttractionSpeed;
        }

        transform.position += _velocity * Time.deltaTime;
    }

    private static void BuildScatterBasis(Vector3 forward, out Vector3 side, out Vector3 lift)
    {
        Vector3 reference = Mathf.Abs(forward.y) > 0.8f ? Vector3.right : Vector3.up;
        side = Vector3.Cross(reference, forward).normalized;
        lift = Vector3.Cross(forward, side).normalized;
    }

    private static Vector3 SampleEscapeScatter(Vector3 burstDirection, Vector3 velocity, float seed, float age, float flutterFrequency, float chaosFrequency)
    {
        Vector3 forward = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : burstDirection;
        BuildScatterBasis(forward, out Vector3 side, out Vector3 lift);

        float flutterPhase = age * flutterFrequency + seed;
        float chaosPhase = age * chaosFrequency + seed * 1.73f;
        float sideAmount = Mathf.Sin(flutterPhase) * 0.18f + Mathf.Sin(chaosPhase * 1.2f) * 0.22f;
        float liftAmount = 0.08f + Mathf.Abs(Mathf.Cos(flutterPhase * 0.85f + seed * 0.4f)) * 0.16f + Mathf.Sin(chaosPhase * 1.05f) * 0.05f;
        float surge = 1.0f + 0.08f * Mathf.Sin(chaosPhase * 0.6f + seed * 0.5f);
        Vector3 scatter = forward * surge + side * sideAmount + lift * liftAmount;

        return scatter.sqrMagnitude > 0.0001f ? scatter.normalized : forward;
    }

    private Vector3 ComputeSeparationForce()
    {
        if (_separationRadius <= 0f)
        {
            return Vector3.zero;
        }

        Vector3 force = Vector3.zero;
        int neighborCount = 0;
        float radiusSqr = _separationRadius * _separationRadius;

        for (int i = 0; i < ActiveDrifts.Count; i++)
        {
            CpuBurstDrift other = ActiveDrifts[i];
            if (other == null || other == this)
            {
                continue;
            }

            Vector3 away = transform.position - other.transform.position;
            float distanceSqr = away.sqrMagnitude;
            if (distanceSqr <= 0.0001f || distanceSqr > radiusSqr)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSqr);
            float weight = 1f - distance / _separationRadius;
            force += away / distance * weight;
            neighborCount++;
        }

        if (neighborCount == 0 || force.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        return force / neighborCount;
    }
}
