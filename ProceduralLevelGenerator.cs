using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Procedural Level Generator — Unity Editor Script
/// Place in any Editor/ folder. Open via: Tools > Procedural Level Generator
/// </summary>
public class ProceduralLevelGenerator : EditorWindow
{
    // ─── Terrain Settings ────────────────────────────────────────────────────
    private int terrainWidth = 512;
    private int terrainLength = 512;
    private int terrainHeight = 200;
    private int heightmapRes = 513;

    // ─── Noise Settings ──────────────────────────────────────────────────────
    private float perlinScale = 3.5f;
    private int perlinOctaves = 6;
    private float perlinPersistence = 0.45f;
    private float perlinLacunarity = 1.8f;
    private float ridgePower = 1.8f;
    private float domainWarpStrength = 0.30f;
    private int smoothIterations = 3;
    private int smoothRadius = 2;
    private int randomSeed = 42;
    private bool useRandomSeed = false;

    // ─── Texture Settings ────────────────────────────────────────────────────
    private bool paintTextures = true;
    private float grassMaxHeight = 0.28f;
    private float rockMinSlope = 28f;
    private float snowMinHeight = 0.65f;

    // ─── Tree Settings ───────────────────────────────────────────────────────
    [System.Serializable]
    public class TreeLayer
    {
        public GameObject prefab = null;
        public int count = 200;
        public float minHeight = 0.05f;
        public float maxHeight = 0.50f;
        public float minSlope = 0f;
        public float maxSlope = 28f;
        public float minScale = 0.6f;
        public float maxScale = 1.4f;
        public bool foldout = true;
    }

    private bool generateTrees = true;
    private float treeBendFactor = 0.4f;
    private List<TreeLayer> treeLayers = new List<TreeLayer> { new TreeLayer() };

    // ─── Detail / Grass ──────────────────────────────────────────────────────
    private bool generateGrassDetail = false;
    private int detailResolution = 512;
    private int detailDensity = 8;

    // ─── Water ───────────────────────────────────────────────────────────────
    private bool addWaterPlane = true;
    private float waterHeightNorm = 0.16f;

    // ─── Internal ────────────────────────────────────────────────────────────
    private Vector2 scrollPos;
    private Terrain generatedTerrain;

    [MenuItem("Tools/Procedural Level Generator")]
    public static void ShowWindow()
    {
        var w = GetWindow<ProceduralLevelGenerator>("Level Generator");
        w.minSize = new Vector2(370, 580);
    }

    // ─── GUI ─────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUIStyle header = new GUIStyle(EditorStyles.boldLabel)
        { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        GUILayout.Space(8);
        GUILayout.Label("🌍  Procedural Level Generator", header);
        GUILayout.Space(4);
        DrawLine();

        // ── Terrain ──────────────────────────────────────────────────────────
        GUILayout.Label("Terrain", EditorStyles.boldLabel);
        terrainWidth = EditorGUILayout.IntField("Width  (m)", terrainWidth);
        terrainLength = EditorGUILayout.IntField("Length (m)", terrainLength);
        terrainHeight = EditorGUILayout.IntField("Max Height (m)", terrainHeight);
        heightmapRes = EditorGUILayout.IntField("Heightmap Res", heightmapRes);
        GUILayout.Space(6);

        // ── Noise ────────────────────────────────────────────────────────────
        DrawLine();
        GUILayout.Label("Noise", EditorStyles.boldLabel);
        perlinScale = EditorGUILayout.FloatField("Scale", perlinScale);
        perlinOctaves = EditorGUILayout.IntSlider("Octaves", perlinOctaves, 1, 10);
        perlinPersistence = EditorGUILayout.Slider("Persistence", perlinPersistence, 0.1f, 1f);
        perlinLacunarity = EditorGUILayout.Slider("Lacunarity", perlinLacunarity, 1f, 4f);
        GUILayout.Space(4);
        ridgePower = EditorGUILayout.Slider("Ridge Power", ridgePower, 1f, 4f);
        EditorGUILayout.HelpBox("Ridge Power >1 compresses valleys and sharpens peaks. ~2 = mountains, ~3 = dramatic ridges.", MessageType.None);
        GUILayout.Space(4);
        domainWarpStrength = EditorGUILayout.Slider("Domain Warp", domainWarpStrength, 0f, 1f);
        EditorGUILayout.HelpBox("Domain Warp bends the noise space to create winding valleys and organic ridges.", MessageType.None);
        GUILayout.Space(4);
        smoothIterations = EditorGUILayout.IntSlider("Smooth Passes", smoothIterations, 0, 8);
        smoothRadius = EditorGUILayout.IntSlider("Smooth Radius", smoothRadius, 1, 5);
        EditorGUILayout.HelpBox("Box-blur after generation. 2-3 passes at radius 2 removes staircasing.", MessageType.None);
        GUILayout.Space(4);
        useRandomSeed = EditorGUILayout.Toggle("Random Seed", useRandomSeed);
        if (!useRandomSeed)
            randomSeed = EditorGUILayout.IntField("Seed", randomSeed);
        GUILayout.Space(6);

        // ── Textures ─────────────────────────────────────────────────────────
        DrawLine();
        GUILayout.Label("Terrain Textures", EditorStyles.boldLabel);
        paintTextures = EditorGUILayout.Toggle("Auto-paint layers", paintTextures);
        if (paintTextures)
        {
            grassMaxHeight = EditorGUILayout.Slider("Grass max height", grassMaxHeight, 0f, 1f);
            rockMinSlope = EditorGUILayout.Slider("Rock min slope (°)", rockMinSlope, 0f, 90f);
            snowMinHeight = EditorGUILayout.Slider("Snow min height", snowMinHeight, 0f, 1f);
            EditorGUILayout.HelpBox("Procedural solid-colour layers. Swap TerrainLayer assets in the Inspector for real textures.", MessageType.Info);
        }
        GUILayout.Space(6);

        // ── Trees ─────────────────────────────────────────────────────────────
        DrawLine();
        GUILayout.Label("Trees", EditorStyles.boldLabel);
        generateTrees = EditorGUILayout.Toggle("Generate Trees", generateTrees);

        if (generateTrees)
        {
            treeBendFactor = EditorGUILayout.Slider("Bend Factor (all)", treeBendFactor, 0f, 1f);
            GUILayout.Space(4);

            // Per-layer UI
            for (int i = 0; i < treeLayers.Count; i++)
            {
                TreeLayer layer = treeLayers[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Header row with foldout + remove button
                EditorGUILayout.BeginHorizontal();
                string label = layer.prefab != null ? layer.prefab.name : $"Tree Layer {i + 1}";
                layer.foldout = EditorGUILayout.Foldout(layer.foldout, $"🌲  {label}", true, EditorStyles.foldoutHeader);
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(18)))
                {
                    treeLayers.RemoveAt(i);
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                if (layer.foldout)
                {
                    EditorGUI.indentLevel++;
                    layer.prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", layer.prefab, typeof(GameObject), false);
                    layer.count = EditorGUILayout.IntField("Count", layer.count);
                    GUILayout.Space(2);
                    layer.minHeight = EditorGUILayout.Slider("Min Terrain H", layer.minHeight, 0f, 1f);
                    layer.maxHeight = EditorGUILayout.Slider("Max Terrain H", layer.maxHeight, 0f, 1f);
                    layer.minSlope = EditorGUILayout.Slider("Min Slope (°)", layer.minSlope, 0f, 90f);
                    layer.maxSlope = EditorGUILayout.Slider("Max Slope (°)", layer.maxSlope, 0f, 90f);
                    GUILayout.Space(2);
                    layer.minScale = EditorGUILayout.Slider("Min Scale", layer.minScale, 0.1f, 5f);
                    layer.maxScale = EditorGUILayout.Slider("Max Scale", layer.maxScale, 0.1f, 5f);
                    if (layer.prefab == null)
                        EditorGUILayout.HelpBox("No prefab — capsule placeholder will be used.", MessageType.Warning);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(2);
            }

            GUILayout.Space(2);
            GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button("+ Add Tree Layer", GUILayout.Height(24)))
                treeLayers.Add(new TreeLayer());
            GUI.backgroundColor = Color.white;
        }
        GUILayout.Space(6);

        // ── Grass Detail ──────────────────────────────────────────────────────
        DrawLine();
        GUILayout.Label("Grass Detail (optional)", EditorStyles.boldLabel);
        generateGrassDetail = EditorGUILayout.Toggle("Generate Grass", generateGrassDetail);
        if (generateGrassDetail)
        {
            detailResolution = EditorGUILayout.IntField("Detail Res", detailResolution);
            detailDensity = EditorGUILayout.IntSlider("Density", detailDensity, 1, 16);
        }
        GUILayout.Space(6);

        // ── Water ─────────────────────────────────────────────────────────────
        DrawLine();
        GUILayout.Label("Water", EditorStyles.boldLabel);
        addWaterPlane = EditorGUILayout.Toggle("Add Water Plane", addWaterPlane);
        if (addWaterPlane)
            waterHeightNorm = EditorGUILayout.Slider("Water Height", waterHeightNorm, 0f, 1f);
        GUILayout.Space(8);

        // ── Buttons ───────────────────────────────────────────────────────────
        DrawLine();
        GUILayout.Space(4);
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
        if (GUILayout.Button("▶  Generate Level", GUILayout.Height(36)))
            GenerateLevel();
        GUI.backgroundColor = Color.white;

        GUILayout.Space(4);
        EditorGUI.BeginDisabledGroup(generatedTerrain == null);
        GUI.backgroundColor = new Color(1f, 0.5f, 0.3f);
        if (GUILayout.Button("🗑  Clear Generated Terrain", GUILayout.Height(28)))
            ClearTerrain();
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();
        GUILayout.Space(10);

        EditorGUILayout.EndScrollView();
    }

    private static void DrawLine()
    {
        var r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        GUILayout.Space(4);
    }

    // ─── Core Generation ─────────────────────────────────────────────────────
    private void GenerateLevel()
    {
        int seed = useRandomSeed ? Random.Range(0, 999999) : randomSeed;
        Random.InitState(seed);

        heightmapRes = Mathf.ClosestPowerOfTwo(heightmapRes - 1) + 1;

        TerrainData td = new TerrainData
        {
            heightmapResolution = heightmapRes,
            size = new Vector3(terrainWidth, terrainHeight, terrainLength)
        };

        float[,] heights = GenerateHeightmap(seed, td.heightmapResolution);
        if (smoothIterations > 0)
            heights = SmoothHeightmap(heights, smoothIterations, smoothRadius);
        td.SetHeights(0, 0, heights);

        if (paintTextures) ApplyTextureLayers(td, heights);
        if (generateTrees) PlaceTrees(td, heights, seed);
        if (generateGrassDetail) AddGrassDetail(td);

        ClearTerrain();
        GameObject go = Terrain.CreateTerrainGameObject(td);
        go.name = "ProceduralTerrain";
        generatedTerrain = go.GetComponent<Terrain>();
        generatedTerrain.Flush();

        if (addWaterPlane) CreateWaterPlane(go.transform);

        Undo.RegisterCreatedObjectUndo(go, "Generate Procedural Level");
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log($"[ProceduralLevelGen] Done | seed={seed} | trees={td.treeInstanceCount}");
    }

    // ─── Heightmap ───────────────────────────────────────────────────────────
    private float[,] GenerateHeightmap(int seed, int res)
    {
        float[,] h = new float[res, res];
        Vector2 mainOffset = new Vector2(seed * 0.137f, seed * 0.241f);
        Vector2 warpOffset = new Vector2(seed * 0.397f + 100f, seed * 0.513f + 200f);

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                float ny = (float)y / (res - 1);

                float wx = 0f, wy = 0f;
                if (domainWarpStrength > 0f)
                {
                    wx = (Mathf.PerlinNoise(nx * perlinScale + warpOffset.x,
                                            ny * perlinScale + warpOffset.y) - 0.5f) * 2f * domainWarpStrength;
                    wy = (Mathf.PerlinNoise(nx * perlinScale + warpOffset.x + 5.3f,
                                            ny * perlinScale + warpOffset.y + 5.3f) - 0.5f) * 2f * domainWarpStrength;
                }

                float raw = FractalNoise(nx + wx, ny + wy, mainOffset);
                float powered = Mathf.Pow(Mathf.Max(raw, 0f), ridgePower);
                float blend = Mathf.Clamp01((ridgePower - 1f) / 3f);
                h[y, x] = Mathf.Clamp(Mathf.Lerp(raw, powered, blend), 0.001f, 1f);
            }
        return h;
    }

    // ─── Smoothing Pass ──────────────────────────────────────────────────────
    private static float[,] SmoothHeightmap(float[,] src, int iterations, int radius)
    {
        int res = src.GetLength(0);
        float[,] a = new float[res, res];
        System.Array.Copy(src, a, src.Length);
        float[,] b = new float[res, res];

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float sum = 0f; int count = 0;
                    int y0 = Mathf.Max(0, y - radius), y1 = Mathf.Min(res - 1, y + radius);
                    int x0 = Mathf.Max(0, x - radius), x1 = Mathf.Min(res - 1, x + radius);
                    for (int ky = y0; ky <= y1; ky++)
                        for (int kx = x0; kx <= x1; kx++) { sum += a[ky, kx]; count++; }
                    b[y, x] = Mathf.Clamp(sum / count, 0.001f, 1f);
                }
            float[,] tmp = a; a = b; b = tmp;
        }
        return a;
    }

    // ─── Fractal Brownian Motion ──────────────────────────────────────────────
    private float FractalNoise(float nx, float ny, Vector2 offset)
    {
        float value = 0f, amp = 1f, freq = 1f, maxValue = 0f;
        for (int i = 0; i < perlinOctaves; i++)
        {
            float px = nx * perlinScale * freq + offset.x + i * 31.71f;
            float py = ny * perlinScale * freq + offset.y + i * 17.43f;
            value += Mathf.PerlinNoise(px, py) * amp;
            maxValue += amp;
            amp *= perlinPersistence;
            freq *= perlinLacunarity;
        }
        return value / maxValue;
    }

    // ─── Texture Layers ──────────────────────────────────────────────────────
    private void ApplyTextureLayers(TerrainData td, float[,] heights)
    {
        int res = td.alphamapResolution;
        int hRes = heights.GetLength(0);

        TerrainLayer grassLayer = CreateTerrainLayer("Grass", new Color(0.28f, 0.52f, 0.18f));
        TerrainLayer rockLayer = CreateTerrainLayer("Rock", new Color(0.48f, 0.43f, 0.36f));
        TerrainLayer snowLayer = CreateTerrainLayer("Snow", new Color(0.92f, 0.95f, 0.98f));
        td.terrainLayers = new TerrainLayer[] { grassLayer, rockLayer, snowLayer };

        float[,,] splatmap = new float[res, res, 3];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float nx = (float)x / (res - 1);
                float ny = (float)y / (res - 1);
                int hx = Mathf.Clamp(Mathf.RoundToInt(nx * (hRes - 1)), 0, hRes - 1);
                int hy = Mathf.Clamp(Mathf.RoundToInt(ny * (hRes - 1)), 0, hRes - 1);
                float normH = heights[hy, hx];
                float slope = td.GetSteepness(nx, ny);

                float wGrass, wRock, wSnow;
                if (normH >= snowMinHeight)
                {
                    float t = Mathf.InverseLerp(snowMinHeight, 1f, normH);
                    wSnow = t; wRock = 1f - t; wGrass = 0f;
                }
                else if (slope >= rockMinSlope) { wRock = 1f; wGrass = 0f; wSnow = 0f; }
                else if (normH <= grassMaxHeight) { wGrass = 1f; wRock = 0f; wSnow = 0f; }
                else
                {
                    float t = Mathf.InverseLerp(grassMaxHeight, snowMinHeight, normH);
                    wGrass = 1f - t; wRock = t; wSnow = 0f;
                }

                splatmap[y, x, 0] = wGrass;
                splatmap[y, x, 1] = wRock;
                splatmap[y, x, 2] = wSnow;
            }
        td.SetAlphamaps(0, 0, splatmap);
    }

    private TerrainLayer CreateTerrainLayer(string layerName, Color colour)
    {
        Texture2D tex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = colour;
        tex.SetPixels(pixels); tex.Apply(); tex.name = layerName + "Tex";
        return new TerrainLayer { name = layerName, diffuseTexture = tex, tileSize = new Vector2(15, 15) };
    }

    // ─── Tree Placement ──────────────────────────────────────────────────────
    private void PlaceTrees(TerrainData td, float[,] heights, int seed)
    {
        if (treeLayers == null || treeLayers.Count == 0) return;

        // Build one TreePrototype per layer (deduplicate same prefab if needed)
        var prototypes = new List<TreePrototype>();
        var layerProtoIndex = new int[treeLayers.Count];

        for (int i = 0; i < treeLayers.Count; i++)
        {
            GameObject proto = treeLayers[i].prefab != null
                ? treeLayers[i].prefab
                : CreateCapsulePlaceholder();

            // Reuse existing prototype index if same prefab was already added
            int found = -1;
            for (int p = 0; p < prototypes.Count; p++)
                if (prototypes[p].prefab == proto) { found = p; break; }

            if (found >= 0)
            {
                layerProtoIndex[i] = found;
            }
            else
            {
                layerProtoIndex[i] = prototypes.Count;
                prototypes.Add(new TreePrototype { prefab = proto, bendFactor = treeBendFactor });
            }
        }

        td.treePrototypes = prototypes.ToArray();

        int hRes = heights.GetLength(0);
        var allInstances = new List<TreeInstance>();

        // Place trees for each layer independently with its own RNG stream
        for (int i = 0; i < treeLayers.Count; i++)
        {
            TreeLayer layer = treeLayers[i];
            float minH = Mathf.Min(layer.minHeight, layer.maxHeight);
            float maxH = Mathf.Max(layer.minHeight, layer.maxHeight);
            float minSc = Mathf.Min(layer.minScale, layer.maxScale);
            float maxSc = Mathf.Max(layer.minScale, layer.maxScale);
            int protoIdx = layerProtoIndex[i];

            // Offset seed per layer so each gets a unique spatial distribution
            var rng = new System.Random(seed + 7 + i * 1000);
            int placed = 0;

            for (int attempt = 0; attempt < layer.count * 10 && placed < layer.count; attempt++)
            {
                float nx = (float)rng.NextDouble();
                float ny = (float)rng.NextDouble();
                int hx = Mathf.Clamp(Mathf.RoundToInt(nx * (hRes - 1)), 0, hRes - 1);
                int hy = Mathf.Clamp(Mathf.RoundToInt(ny * (hRes - 1)), 0, hRes - 1);
                float normH = heights[hy, hx];
                float slope = td.GetSteepness(nx, ny);

                if (normH < minH || normH > maxH) continue;
                if (slope < layer.minSlope || slope > layer.maxSlope) continue;

                float scale = minSc + (float)rng.NextDouble() * (maxSc - minSc);
                allInstances.Add(new TreeInstance
                {
                    position = new Vector3(nx, normH, ny),
                    prototypeIndex = protoIdx,
                    widthScale = scale,
                    heightScale = scale,
                    color = Color.white,
                    lightmapColor = Color.white
                });
                placed++;
            }

            Debug.Log($"[ProceduralLevelGen] Layer {i} ({(layer.prefab != null ? layer.prefab.name : "placeholder")}): placed {placed}/{layer.count} trees.");
        }

        td.SetTreeInstances(allInstances.ToArray(), true);
    }

    private static GameObject CreateCapsulePlaceholder()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "TreePlaceholder";
        go.hideFlags = HideFlags.HideAndDontSave;
        return go;
    }

    // ─── Grass Detail ────────────────────────────────────────────────────────
    private void AddGrassDetail(TerrainData td)
    {
        td.SetDetailResolution(detailResolution, 16);
        DetailPrototype dp = new DetailPrototype
        {
            usePrototypeMesh = false,
            prototypeTexture = CreateGrassTexture(),
            minHeight = 0.2f,
            maxHeight = 0.6f,
            minWidth = 0.2f,
            maxWidth = 0.5f,
            healthyColor = new Color(0.3f, 0.8f, 0.2f),
            dryColor = new Color(0.8f, 0.7f, 0.2f),
            renderMode = DetailRenderMode.GrassBillboard,
            noiseSpread = 0.1f
        };
        td.detailPrototypes = new DetailPrototype[] { dp };

        int res = td.detailResolution;
        int[,] layer = new int[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                layer[y, x] = detailDensity;
        td.SetDetailLayer(0, 0, 0, layer);
    }

    private static Texture2D CreateGrassTexture()
    {
        Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float t = y / 63f;
                tex.SetPixel(x, y, Color.Lerp(
                    new Color(0.25f, 0.55f, 0.15f, t > 0.05f ? 1f : 0f),
                    new Color(0.45f, 0.75f, 0.25f, t > 0.80f ? 0f : 1f), t));
            }
        tex.Apply(); tex.name = "GrassTex";
        return tex;
    }

    // ─── Water ───────────────────────────────────────────────────────────────
    private void CreateWaterPlane(Transform parent)
    {
        float worldY = waterHeightNorm * terrainHeight;
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "WaterPlane";
        water.transform.SetParent(parent);
        water.transform.localScale = new Vector3(terrainWidth / 10f, 1f, terrainLength / 10f);
        water.transform.localPosition = new Vector3(terrainWidth / 2f, worldY, terrainLength / 2f);

        Material mat = new Material(Shader.Find("Standard")) { color = new Color(0.05f, 0.35f, 0.75f, 0.6f) };
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        water.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ─── Clear ────────────────────────────────────────────────────────────────
    private void ClearTerrain()
    {
        var existing = GameObject.Find("ProceduralTerrain");
        if (existing != null) { DestroyImmediate(existing); generatedTerrain = null; }
    }
}