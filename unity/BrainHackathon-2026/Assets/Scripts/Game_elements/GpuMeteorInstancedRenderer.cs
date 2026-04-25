using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class GpuMeteorInstancedRenderer : MonoBehaviour
{
    private const string DefaultComputeResource = "GpuMeteorSimulation";
    private const string DefaultShaderResource = "GpuMeteorInstancedURP";
    private const int ThreadsPerGroup = 64;
    private const int MeteorStateStride = sizeof(float) * 8;
    private const int InstanceDataStride = sizeof(float) * 4;

    private static readonly int StatesId = Shader.PropertyToID("_States");
    private static readonly int InstanceDataId = Shader.PropertyToID("_InstanceData");
    private static readonly int InstanceCountId = Shader.PropertyToID("_InstanceCount");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int TimeId = Shader.PropertyToID("_Time");
    private static readonly int TargetPositionId = Shader.PropertyToID("_TargetPosition");
    private static readonly int StoppingDistanceId = Shader.PropertyToID("_StoppingDistance");
    private static readonly int SpawnXMinId = Shader.PropertyToID("_SpawnXMin");
    private static readonly int SpawnXMaxId = Shader.PropertyToID("_SpawnXMax");
    private static readonly int SpawnYId = Shader.PropertyToID("_SpawnY");
    private static readonly int SpawnZId = Shader.PropertyToID("_SpawnZ");
    private static readonly int MinSpeedId = Shader.PropertyToID("_MinSpeed");
    private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
    private static readonly int MinScaleId = Shader.PropertyToID("_MinScale");
    private static readonly int MaxScaleId = Shader.PropertyToID("_MaxScale");
    private static readonly int MinLifetimeId = Shader.PropertyToID("_MinLifetime");
    private static readonly int MaxLifetimeId = Shader.PropertyToID("_MaxLifetime");
    private static readonly int RootMatrixId = Shader.PropertyToID("_RootMatrix");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    [Header("Source Prefab")]
    [Tooltip("Only the prefab's mesh and material are used. This does not create GameObject instances.")]
    public GameObject meteorPrefab;

    [Header("Target")]
    [Tooltip("Meteors move toward this target. Leave empty to auto-find by tag 'Player'.")]
    public Transform target;

    [Tooltip("Optional override. Leave empty to draw in all cameras.")]
    public Camera renderCamera;

    [Header("Instance Count")]
    [Min(1)] public int instanceCount = 2048;

    [Tooltip("Extra distance above the top of the screen to spawn.")]
    [Min(0f)] public float spawnYOffset = 1f;

    [Tooltip("Extra padding added to the procedural draw bounds.")]
    [Min(0f)] public float boundsPadding = 2f;

    [Header("Motion")]
    [Min(0f)] public float minSpeed = 2f;
    [Min(0f)] public float maxSpeed = 6f;

    [Tooltip("Instances respawn when they get this close to the target.")]
    [Min(0f)] public float stoppingDistance = 0.25f;

    [Min(0f)] public float minLifetime = 3f;
    [Min(0f)] public float maxLifetime = 8f;

    [Header("Scale")]
    [Min(0f)] public float minScale = 0.5f;
    [Min(0f)] public float maxScale = 1.5f;

    [Header("GPU Assets")]
    [Tooltip("Optional. If left empty the component loads Assets/Resources/GpuMeteorSimulation.compute.")]
    public ComputeShader simulationShader;

    [Tooltip("Optional. If left empty the component loads Assets/Resources/GpuMeteorInstancedURP.shader.")]
    public Shader instancedShader;

    private ComputeShader _resolvedSimulationShader;
    private Shader _resolvedInstancedShader;
    private ComputeBuffer _stateBuffer;
    private ComputeBuffer _instanceDataBuffer;
    private Material _drawMaterial;
    private MaterialPropertyBlock _propertyBlock;
    private Mesh _sourceMesh;
    private int _initializeKernel;
    private int _updateKernel;
    private int _dispatchGroupCount;
    private int _allocatedInstanceCount;
    private Bounds _drawBounds;
    private bool _isInitialized;
    private bool _loggedSetupWarning;

    private void OnEnable()
    {
        TryInitialize();
    }

    private void Update()
    {
        if (!TryInitialize())
        {
            return;
        }

        if (!TryBuildSpawnData(out float spawnXMin, out float spawnXMax, out float spawnY, out float spawnZ))
        {
            return;
        }

        SetSimulationParameters(spawnXMin, spawnXMax, spawnY, spawnZ);
        _resolvedSimulationShader.Dispatch(_updateKernel, _dispatchGroupCount, 1, 1);

        _propertyBlock.SetBuffer(InstanceDataId, _instanceDataBuffer);
        _propertyBlock.SetMatrix(RootMatrixId, transform.localToWorldMatrix);
        Graphics.DrawMeshInstancedProcedural(
            _sourceMesh,
            0,
            _drawMaterial,
            _drawBounds,
            instanceCount,
            _propertyBlock,
            ShadowCastingMode.Off,
            false,
            gameObject.layer,
            renderCamera);
    }

    private void OnDisable()
    {
        ReleaseResources();
    }

    private void OnDestroy()
    {
        ReleaseResources();
    }

    private void OnValidate()
    {
        instanceCount = Mathf.Max(1, instanceCount);
        maxSpeed = Mathf.Max(minSpeed, maxSpeed);
        maxScale = Mathf.Max(minScale, maxScale);
        maxLifetime = Mathf.Max(minLifetime, maxLifetime);
    }

    private bool TryInitialize()
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            LogSetupWarning("Compute shaders are not supported on this device.");
            return false;
        }

        if (!SystemInfo.supportsInstancing)
        {
            LogSetupWarning("GPU instancing is not supported on this device.");
            return false;
        }

        ResolveTarget();

        if (_isInitialized && _allocatedInstanceCount == instanceCount)
        {
            return true;
        }

        ReleaseResources();

        if (!TryResolveAssets(out Mesh sourceMesh, out Material sourceMaterial))
        {
            return false;
        }

        if (!TryBuildSpawnData(out float spawnXMin, out float spawnXMax, out float spawnY, out float spawnZ))
        {
            return false;
        }

        _sourceMesh = sourceMesh;
        _propertyBlock = new MaterialPropertyBlock();
        _drawMaterial = new Material(_resolvedInstancedShader)
        {
            enableInstancing = true,
            hideFlags = HideFlags.DontSave,
            name = "GpuMeteorInstancedRuntimeMaterial"
        };

        CopyCommonMaterialProperties(sourceMaterial, _drawMaterial);

        _stateBuffer = new ComputeBuffer(instanceCount, MeteorStateStride);
        _instanceDataBuffer = new ComputeBuffer(instanceCount, InstanceDataStride);
        _dispatchGroupCount = Mathf.CeilToInt(instanceCount / (float)ThreadsPerGroup);
        _allocatedInstanceCount = instanceCount;

        _initializeKernel = _resolvedSimulationShader.FindKernel("InitializeMeteors");
        _updateKernel = _resolvedSimulationShader.FindKernel("UpdateMeteors");

        BindBuffers(_initializeKernel);
        BindBuffers(_updateKernel);

        SetSimulationParameters(spawnXMin, spawnXMax, spawnY, spawnZ);
        _resolvedSimulationShader.Dispatch(_initializeKernel, _dispatchGroupCount, 1, 1);
        _propertyBlock.SetBuffer(InstanceDataId, _instanceDataBuffer);

        _isInitialized = true;
        _loggedSetupWarning = false;
        return true;
    }

    private void ResolveTarget()
    {
        if (target != null)
        {
            return;
        }

        GameObject found = GameObject.FindWithTag("Player");
        if (found != null)
        {
            target = found.transform;
        }
    }

    private bool TryResolveAssets(out Mesh sourceMesh, out Material sourceMaterial)
    {
        sourceMesh = null;
        sourceMaterial = null;

        if (meteorPrefab == null)
        {
            LogSetupWarning("Assign a meteor prefab with a MeshFilter and MeshRenderer.");
            return false;
        }

        if (!meteorPrefab.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
        {
            LogSetupWarning("The meteor prefab is missing a MeshFilter or shared mesh.");
            return false;
        }

        if (!meteorPrefab.TryGetComponent(out MeshRenderer meshRenderer) || meshRenderer.sharedMaterial == null)
        {
            LogSetupWarning("The meteor prefab is missing a MeshRenderer or shared material.");
            return false;
        }

        _resolvedSimulationShader = simulationShader != null
            ? simulationShader
            : Resources.Load<ComputeShader>(DefaultComputeResource);

        if (_resolvedSimulationShader == null)
        {
            LogSetupWarning("Could not find the compute shader. Assign it in the inspector or keep Assets/Resources/GpuMeteorSimulation.compute.");
            return false;
        }

        _resolvedInstancedShader = instancedShader != null
            ? instancedShader
            : Resources.Load<Shader>(DefaultShaderResource);

        if (_resolvedInstancedShader == null)
        {
            LogSetupWarning("Could not find the instanced URP shader. Assign it in the inspector or keep Assets/Resources/GpuMeteorInstancedURP.shader.");
            return false;
        }

        sourceMesh = meshFilter.sharedMesh;
        sourceMaterial = meshRenderer.sharedMaterial;
        return true;
    }

    private bool TryBuildSpawnData(out float spawnXMin, out float spawnXMax, out float spawnY, out float spawnZ)
    {
        spawnXMin = 0f;
        spawnXMax = 0f;
        spawnY = 0f;
        float worldSpawnZ = target != null ? target.position.z : 0f;
        spawnZ = worldSpawnZ;

        Camera activeCamera = renderCamera != null ? renderCamera : Camera.main;
        if (activeCamera == null)
        {
            LogSetupWarning("No render camera is available. Assign one or tag a camera as MainCamera.");
            return false;
        }

        float distToSpawnPlane = Mathf.Abs(activeCamera.transform.position.z - worldSpawnZ);
        Vector3 worldLeft = activeCamera.ScreenToWorldPoint(new Vector3(0f, Screen.height, distToSpawnPlane));
        Vector3 worldRight = activeCamera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, distToSpawnPlane));
        Vector3 worldTop = activeCamera.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height, distToSpawnPlane));
        Vector3 worldBottom = activeCamera.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, 0f, distToSpawnPlane));
        Vector3 localLeft = transform.InverseTransformPoint(worldLeft);
        Vector3 localRight = transform.InverseTransformPoint(worldRight);
        Vector3 localTop = transform.InverseTransformPoint(worldTop);
        Vector3 localBottom = transform.InverseTransformPoint(worldBottom);
        Vector3 localTarget = target != null ? transform.InverseTransformPoint(target.position) : localBottom;

        spawnXMin = Mathf.Min(localLeft.x, localRight.x);
        spawnXMax = Mathf.Max(localLeft.x, localRight.x);
        spawnY = localTop.y + spawnYOffset;
        spawnZ = localTarget.z;

        float worldTargetY = target != null ? target.position.y : worldBottom.y;
        float worldSpawnY = worldTop.y + spawnYOffset;
        float minY = Mathf.Min(worldBottom.y, worldTargetY) - boundsPadding;
        float width = Mathf.Abs(worldRight.x - worldLeft.x) + boundsPadding * 2f;
        float height = Mathf.Abs(worldSpawnY - minY) + boundsPadding * 2f;

        _drawBounds = new Bounds(
            new Vector3((worldLeft.x + worldRight.x) * 0.5f, minY + height * 0.5f, worldSpawnZ),
            new Vector3(width, height, 32f));

        return true;
    }

    private void SetSimulationParameters(float spawnXMin, float spawnXMax, float spawnY, float spawnZ)
    {
        Vector3 targetPosition = target != null
            ? transform.InverseTransformPoint(target.position)
            : new Vector3(0f, 0f, spawnZ);

        _resolvedSimulationShader.SetInt(InstanceCountId, instanceCount);
        _resolvedSimulationShader.SetFloat(DeltaTimeId, Time.deltaTime);
        _resolvedSimulationShader.SetFloat(TimeId, Time.time);
        _resolvedSimulationShader.SetVector(TargetPositionId, targetPosition);
        _resolvedSimulationShader.SetFloat(StoppingDistanceId, stoppingDistance);
        _resolvedSimulationShader.SetFloat(SpawnXMinId, spawnXMin);
        _resolvedSimulationShader.SetFloat(SpawnXMaxId, spawnXMax);
        _resolvedSimulationShader.SetFloat(SpawnYId, spawnY);
        _resolvedSimulationShader.SetFloat(SpawnZId, spawnZ);
        _resolvedSimulationShader.SetFloat(MinSpeedId, minSpeed);
        _resolvedSimulationShader.SetFloat(MaxSpeedId, maxSpeed);
        _resolvedSimulationShader.SetFloat(MinScaleId, minScale);
        _resolvedSimulationShader.SetFloat(MaxScaleId, maxScale);
        _resolvedSimulationShader.SetFloat(MinLifetimeId, minLifetime);
        _resolvedSimulationShader.SetFloat(MaxLifetimeId, maxLifetime);
    }

    private void BindBuffers(int kernel)
    {
        _resolvedSimulationShader.SetBuffer(kernel, StatesId, _stateBuffer);
        _resolvedSimulationShader.SetBuffer(kernel, InstanceDataId, _instanceDataBuffer);
    }

    private void ReleaseResources()
    {
        _isInitialized = false;
        _allocatedInstanceCount = 0;

        ReleaseBuffer(ref _stateBuffer);
        ReleaseBuffer(ref _instanceDataBuffer);

        if (_drawMaterial != null)
        {
            Destroy(_drawMaterial);
            _drawMaterial = null;
        }
    }

    private void LogSetupWarning(string message)
    {
        if (_loggedSetupWarning)
        {
            return;
        }

        Debug.LogWarning($"[GpuMeteorInstancedRenderer] {message}", this);
        _loggedSetupWarning = true;
    }

    private static void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer == null)
        {
            return;
        }

        buffer.Release();
        buffer = null;
    }

    private static void CopyCommonMaterialProperties(Material source, Material destination)
    {
        if (source == null || destination == null)
        {
            return;
        }

        if (source.HasProperty(BaseMapId) && destination.HasProperty(BaseMapId))
        {
            destination.SetTexture(BaseMapId, source.GetTexture(BaseMapId));
        }
        else if (source.HasProperty(MainTexId) && destination.HasProperty(BaseMapId))
        {
            destination.SetTexture(BaseMapId, source.GetTexture(MainTexId));
        }

        if (source.HasProperty(BaseColorId) && destination.HasProperty(BaseColorId))
        {
            destination.SetColor(BaseColorId, source.GetColor(BaseColorId));
            return;
        }

        if (source.HasProperty(ColorId) && destination.HasProperty(BaseColorId))
        {
            destination.SetColor(BaseColorId, source.GetColor(ColorId));
        }
    }
}
