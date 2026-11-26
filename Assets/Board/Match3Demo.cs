using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

// Простой матч-3 для показа эффектов: свайп/клик-клик, совпадения 3+,
// взрыв фишек твоим эффектом, падение, досыпка, каскады.
// Бомба: L/T-матч (или линия из 4+) создаёт бомбу; тап или свап бомбы — взрыв 3x3.
// Клавиши: T — замедление x0.2, B — эффект бомбы под курсором, N — превратить фишку под курсором в бомбу.
public partial class Match3Demo : MonoBehaviour
{
    [Header("Поле")]
    public int width = 8, height = 8;
    public Material[] tileMaterials;
    public Material cellA, cellB, boardBg, mask;

    [Header("Эффекты")]
    public VisualEffectAsset popEffect;
    public string colorProperty = "New Color";
    public float effectScale = 1f;
    public VisualEffectAsset bombEffect;   // если пусто — берётся Assets/match3bomb.vfx
    [Tooltip("Масштаб эффекта бомбы на поле. Крути прямо в Play.")] public float bombEffectScale = 4f;

    [Header("Бомба")]
    public Texture2D bombTexture;          // если пусто — берётся Assets/Board/bomb_tile.png
    [Tooltip("Играть обычный pop-эффект у фишек, снесённых бомбой.")] public bool popEffectOnBomb = false;
    public float bombWaveDelay = 0.045f;   // задержка «волны» сноса фишек на клетку от центра
    public float shakeAmount = 0.12f;
    public float shakeTime = 0.2f;

    [Header("Тайминги")]
    public float swapTime = 0.15f;
    public float popTime = 0.12f;
    public float fallSpeed = 9f;      // клеток в секунду
    public float cascadePause = 0.08f;

    const float TileScale = 0.82f, BombScale = 0.95f;

    class Tile { public Transform t; public int color; public bool bomb, plane; public Vector2Int rocketAxis; public float s = TileScale; }

    Tile[,] grid;
    Camera cam;
    Vector3 camBase;
    Material bombMat;
    bool busy;
    Vector2Int? selected;
    Vector2 pressPos;
    bool pressed;

    Vector3 CellPos(int x, int y) =>
        transform.position + new Vector3(x - (width - 1) * 0.5f, y - (height - 1) * 0.5f, 0);
    Vector3 CellPos(Vector2Int c) => CellPos(c.x, c.y);

    void Start()
    {
        cam = Camera.main;
        camBase = cam.transform.position;
        var tex = bombTexture;
#if UNITY_EDITOR
        if (tex == null) tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Board/bomb_tile.png");
#endif
        bombMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };

        BuildBackground();
        grid = new Tile[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int c;
                do c = Random.Range(0, tileMaterials.Length);
                while ((x >= 2 && grid[x - 1, y].color == c && grid[x - 2, y].color == c) ||
                       (y >= 2 && grid[x, y - 1].color == c && grid[x, y - 2].color == c) ||
                       (x > 0 && y > 0 && ColorAt(x - 1, y) == c &&
                        ColorAt(x, y - 1) == c && ColorAt(x - 1, y - 1) == c));
                grid[x, y] = Spawn(c, CellPos(x, y));
            }
        if (width >= 4 && height >= 3) PlaceStarterPlane();
    }

    void BuildBackground()
    {
        Quad("BoardBg", boardBg, CellPos(0, 0) + new Vector3((width - 1) * 0.5f, (height - 1) * 0.5f, 0.3f),
             new Vector3(width + 0.4f, height + 0.4f, 1));
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                Quad("Cell", (x + y) % 2 == 0 ? cellA : cellB, CellPos(x, y) + Vector3.forward * 0.2f, Vector3.one * 0.98f);
        // маска над полем: новые фишки «выезжают» из-за края
        Quad("TopMask", mask, CellPos(0, height) + new Vector3((width - 1) * 0.5f, height * 0.5f - 0.3f, -0.1f),
             new Vector3(width + 6, height + 0.4f, 1));
    }

    Transform Quad(string name, Material m, Vector3 pos, Vector3 scale)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(transform);
        q.transform.position = pos;
        q.transform.localScale = scale;
        q.GetComponent<Renderer>().sharedMaterial = m;
        return q.transform;
    }

    Tile Spawn(int c, Vector3 pos)
    {
        var t = Quad("Tile", tileMaterials[c], pos, Vector3.one * TileScale);
        return new Tile { t = t, color = c };
    }

    Tile SpawnBomb(Vector3 pos)
    {
        var t = Quad("Bomb", bombMat, pos + Vector3.back * 0.05f, Vector3.one * BombScale);
        return new Tile { t = t, color = -1, bomb = true, s = BombScale };
    }

    // ---------- ввод ----------
    void Update()
    {
        // T — замедление x0.2 для разбора эффектов
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            Time.timeScale = Time.timeScale < 1f ? 1f : 0.2f;

        // покачивание бомб, чтобы было видно, что они «живые»
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var tile = grid[x, y];
                if (tile != null && tile.bomb && tile.t != null)
                    tile.t.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 5f + x * 1.3f + y) * 7f);
            }

        if (Mouse.current == null) return;
        Vector2 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        // B — тест эффекта бомбы: эффект + рамка зоны 3x3 в клетке под курсором
        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
            PlayBomb(CellCenterNearest(world), true);

        if (busy) return;

        // N — превратить фишку под курсором в бомбу (для теста)
        if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
        {
            var c = WorldToCell(world);
            if (c != null && !grid[c.Value.x, c.Value.y].bomb)
            {
                Select(null);
                Destroy(grid[c.Value.x, c.Value.y].t.gameObject);
                grid[c.Value.x, c.Value.y] = SpawnBomb(CellPos(c.Value));
                StartCoroutine(PopIn(grid[c.Value.x, c.Value.y]));
            }
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
        {
            PlaceRecordingRocket(WorldToCell(world), Vector2Int.right);
            return;
        }
        if (keyboard != null && keyboard.vKey.wasPressedThisFrame)
        {
            PlaceRecordingRocket(WorldToCell(world), Vector2Int.up);
            return;
        }

        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame &&
            TryLaunchPaperPlane(WorldToCell(world)))
            return;

        if (Keyboard.current != null && Keyboard.current.oKey.wasPressedThisFrame)
        {
            SetupPlaneSquare();
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            var cell = WorldToCell(world);
            if (cell == null) { Select(null); return; }
            if (selected != null && IsNeighbour(selected.Value, cell.Value))
            {
                var a = selected.Value; Select(null);
                StartCoroutine(TrySwap(a, cell.Value));
                return;
            }
            Select(cell);
            pressPos = world; pressed = true;
        }

        if (pressed && Mouse.current.leftButton.isPressed && selected != null)
        {
            Vector2 d = world - pressPos;
            if (d.magnitude > 0.45f)
            {
                var dir = Mathf.Abs(d.x) > Mathf.Abs(d.y)
                    ? new Vector2Int((int)Mathf.Sign(d.x), 0)
                    : new Vector2Int(0, (int)Mathf.Sign(d.y));
                var a = selected.Value; var b = a + dir;
                pressed = false; Select(null);
                if (InBounds(b)) StartCoroutine(TrySwap(a, b));
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            // тап по бомбе (без свайпа) — взрыв
            if (pressed && selected != null && grid[selected.Value.x, selected.Value.y].bomb &&
                WorldToCell(world) == selected)
            {
                var c = selected.Value; Select(null);
                StartCoroutine(DetonateAndResolve(new List<Vector2Int> { c }));
            }
            else if (pressed && selected.HasValue && WorldToCell(world) == selected &&
                     grid[selected.Value.x, selected.Value.y].plane)
            {
                var cell = selected.Value;
                Select(null);
                StartCoroutine(ActivatePlane(cell));
            }
            else if (pressed && selected.HasValue && WorldToCell(world) == selected &&
                     grid[selected.Value.x, selected.Value.y].rocketAxis != Vector2Int.zero)
            {
                var cell = selected.Value;
                Select(null);
                StartCoroutine(ActivateRocket(cell));
            }
            pressed = false;
        }
    }

    void Select(Vector2Int? c)
    {
        if (selected != null)
        {
            var t = grid[selected.Value.x, selected.Value.y];
            if (t != null) t.t.localScale = Vector3.one * t.s;
        }
        selected = c;
        if (c != null)
        {
            var t = grid[c.Value.x, c.Value.y];
            t.t.localScale = Vector3.one * t.s * 1.15f;
        }
    }

    Vector2Int? WorldToCell(Vector2 w)
    {
        Vector3 local = (Vector3)w - CellPos(0, 0);
        var c = new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y));
        return InBounds(c) ? c : (Vector2Int?)null;
    }

    bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;
    static bool IsNeighbour(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

    // ---------- тест-хуки (для записи видео / проверки из редактора) ----------
    public void DebugPlaceBomb(int x, int y)
    {
        if (busy || !InBounds(new Vector2Int(x, y)) || grid[x, y].bomb) return;
        Destroy(grid[x, y].t.gameObject);
        grid[x, y] = SpawnBomb(CellPos(x, y));
        StartCoroutine(PopIn(grid[x, y]));
    }

    public void DebugDetonate(int x, int y)
    {
        if (busy || !InBounds(new Vector2Int(x, y)) || !grid[x, y].bomb) return;
        StartCoroutine(DetonateAndResolve(new List<Vector2Int> { new Vector2Int(x, y) }));
    }

    // ---------- логика ----------
    IEnumerator TrySwap(Vector2Int a, Vector2Int b)
    {
        busy = true;
        yield return Swap(a, b);

        // свап с бомбой — бомба взрывается всегда, даже без совпадения
        var pending = new Queue<Vector2Int>();
        if (grid[a.x, a.y].color < 0) pending.Enqueue(a);
        if (grid[b.x, b.y].color < 0) pending.Enqueue(b);
        if (pending.Count > 0)
            yield return ResolveBoosterChain(pending, false);
        else if (FindMatches().Count == 0 && FindPlaneSquares().Count == 0)
            yield return Swap(a, b);           // нет совпадения — возвращаем назад
        else
            yield return Resolve(a, b);
        busy = false;
    }

    IEnumerator Swap(Vector2Int a, Vector2Int b)
    {
        var ta = grid[a.x, a.y]; var tb = grid[b.x, b.y];
        grid[a.x, a.y] = tb; grid[b.x, b.y] = ta;
        yield return Tween(new[] { ta.t, tb.t }, new[] { CellPos(b.x, b.y), CellPos(a.x, a.y) }, swapTime);
    }

    int ColorAt(int x, int y) { var t = grid[x, y]; return t == null ? -2 : t.color; }

    // все горизонтальные и вертикальные ряды из 3+ одинаковых фишек (бомбы не участвуют)
    List<List<Vector2Int>> FindRuns()
    {
        var runs = new List<List<Vector2Int>>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; )
            {
                int c = ColorAt(x, y), run = 1;
                if (c >= 0) while (x + run < width && ColorAt(x + run, y) == c) run++;
                if (c >= 0 && run >= 3)
                {
                    var r = new List<Vector2Int>();
                    for (int i = 0; i < run; i++) r.Add(new Vector2Int(x + i, y));
                    runs.Add(r);
                }
                x += run;
            }
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; )
            {
                int c = ColorAt(x, y), run = 1;
                if (c >= 0) while (y + run < height && ColorAt(x, y + run) == c) run++;
                if (c >= 0 && run >= 3)
                {
                    var r = new List<Vector2Int>();
                    for (int i = 0; i < run; i++) r.Add(new Vector2Int(x, y + i));
                    runs.Add(r);
                }
                y += run;
            }
        return runs;
    }

    HashSet<Vector2Int> FindMatches()
    {
        var set = new HashSet<Vector2Int>();
        foreach (var r in FindRuns()) foreach (var c in r) set.Add(c);
        return set;
    }

    // где из этого набора рядов появляются бомбы
    HashSet<Vector2Int> BombSpawns(List<List<Vector2Int>> runs)
    {
        var spawns = new HashSet<Vector2Int>();

        // L/T/+ : горизонтальный и вертикальный ряд одного цвета пересекаются — бомба в точке пересечения
        for (int i = 0; i < runs.Count; i++)
            for (int j = i + 1; j < runs.Count; j++)
            {
                bool hi = runs[i][0].y == runs[i][1].y, hj = runs[j][0].y == runs[j][1].y;
                if (hi == hj) continue;
                foreach (var c in runs[i])
                    if (runs[j].Contains(c)) spawns.Add(c);
            }
        return spawns;
    }

    IEnumerator Resolve(Vector2Int? a = null, Vector2Int? b = null)
    {
        var runs = FindRuns();
        var squares = FindPlaneSquares();
        while (runs.Count > 0 || squares.Count > 0)
        {
            var matched = new HashSet<Vector2Int>();
            foreach (var r in runs) foreach (var c in r) matched.Add(c);
            var spawns = BombSpawns(runs);
            var rockets = PlanRocketSpawns(runs, spawns, a, b);
            var reserved = new HashSet<Vector2Int>(spawns);
            reserved.UnionWith(rockets.Keys);
            var planeSpawns = PlanPlaneSpawns(squares, matched, reserved, a, b);

            foreach (var c in matched)
            {
                var tile = grid[c.x, c.y];
                PlayEffect(CellPos(c), tileMaterials[tile.color].color);
                StartCoroutine(PopAndDestroy(tile.t));
                grid[c.x, c.y] = null;
                if (planeSpawns.Contains(c))
                {
                    grid[c.x, c.y] = SpawnPlane(c);
                    StartCoroutine(PopIn(grid[c.x, c.y]));
                }
                else if (rockets.TryGetValue(c, out var axis))
                {
                    grid[c.x, c.y] = SpawnRocket(c, axis);
                    StartCoroutine(PopIn(grid[c.x, c.y]));
                }
                else if (spawns.Contains(c))
                {
                    grid[c.x, c.y] = SpawnBomb(CellPos(c));
                    StartCoroutine(PopIn(grid[c.x, c.y]));
                }
            }
            yield return new WaitForSeconds(popTime + cascadePause);
            yield return Collapse();
            yield return new WaitForSeconds(cascadePause);
            a = b = null;
            runs = FindRuns();
            squares = FindPlaneSquares();
        }
    }

    // взрыв одной или нескольких бомб + цепная реакция, потом падение и каскады
    IEnumerator DetonateAndResolve(List<Vector2Int> start, bool setBusy = true)
    {
        yield return ResolveBoosterChain(new Queue<Vector2Int>(start), setBusy);
    }

    IEnumerator ResolveBoosterChain(Queue<Vector2Int> pending, bool setBusy = true)
    {
        if (setBusy) busy = true;
        while (pending.Count > 0)
        {
            var cell = pending.Dequeue();
            var tile = grid[cell.x, cell.y];
            if (tile == null || tile.color >= 0) continue;

            grid[cell.x, cell.y] = null;
            if (tile.bomb)
            {
                StartCoroutine(PopAndDestroy(tile.t));
                yield return DetonateBomb(cell, pending);
            }
            else
            {
                Destroy(tile.t.gameObject);
                if (tile.plane)
                    yield return LaunchPlane(cell, pending);
                else
                    yield return LaunchRocket(cell, tile.rocketAxis, pending);
            }
        }

        yield return new WaitForSeconds(Mathf.Max(popTime, 0.25f));
        yield return Collapse();
        yield return new WaitForSeconds(cascadePause);
        yield return Resolve();
        if (setBusy) busy = false;
    }

    IEnumerator DetonateBomb(Vector2Int origin, Queue<Vector2Int> pending)
    {
        PlayBomb(CellPos(origin), false);
        StartCoroutine(Shake());
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                float delay = Mathf.Sqrt(dx * dx + dy * dy) * bombWaveDelay;
                HitBoosterCell(origin + new Vector2Int(dx, dy), pending, delay, popEffectOnBomb);
            }
        yield return new WaitForSeconds(0.12f);
    }

    void HitBoosterCell(Vector2Int cell, Queue<Vector2Int> pending, float delay = 0f, bool playPop = false)
    {
        if (!InBounds(cell)) return;
        var tile = grid[cell.x, cell.y];
        if (tile == null) return;
        if (tile.color < 0)
        {
            if (!pending.Contains(cell)) pending.Enqueue(cell);
            return;
        }

        grid[cell.x, cell.y] = null;
        StartCoroutine(DelayedPop(tile, CellPos(cell), delay, playPop));
    }

    IEnumerator DelayedPop(Tile tile, Vector3 pos, float delay, bool playPop)
    {
        if (delay > 0) yield return new WaitForSeconds(delay);
        if (playPop) PlayEffect(pos, tileMaterials[tile.color].color);
        yield return PopAndDestroy(tile.t);
    }

    // падение и досыпка
    IEnumerator Collapse()
    {
        var moving = new List<Transform>(); var targets = new List<Vector3>();
        for (int x = 0; x < width; x++)
        {
            int write = 0;
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == null) continue;
                if (y != write)
                {
                    grid[x, write] = grid[x, y]; grid[x, y] = null;
                    moving.Add(grid[x, write].t);
                    targets.Add(CellPos(x, write) + ((grid[x, write].color < 0) ? Vector3.back * 0.05f : Vector3.zero));
                }
                write++;
            }
            for (int y = write, k = 0; y < height; y++, k++)
            {
                int c = Random.Range(0, tileMaterials.Length);
                grid[x, y] = Spawn(c, CellPos(x, height + k));
                moving.Add(grid[x, y].t); targets.Add(CellPos(x, y));
            }
        }
        yield return Fall(moving, targets);
    }

    Vector3 CellCenterNearest(Vector3 w)
    {
        Vector3 o = CellPos(0, 0);
        int x = Mathf.Clamp(Mathf.RoundToInt(w.x - o.x), 0, width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(w.y - o.y), 0, height - 1);
        return CellPos(x, y);
    }

    void PlayBomb(Vector3 pos, bool showZone, float scale = 1f)
    {
        var asset = bombEffect;
#if UNITY_EDITOR
        if (asset == null) asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/match3bomb.vfx");
#endif
        if (asset == null) return;
        var go = new GameObject("bomb");
        go.transform.position = pos + Vector3.back * 0.5f;
        go.transform.localScale = Vector3.one * (bombEffectScale * scale);
        var vfx = go.AddComponent<VisualEffect>();
        vfx.visualEffectAsset = asset;
        vfx.Play();
        Destroy(go, 3f);

        if (!showZone) return;
        // рамка зоны 3x3 (только для теста по B)
        var fr = new GameObject("bombZone");
        var lr = fr.AddComponent<LineRenderer>();
        lr.useWorldSpace = true; lr.loop = true; lr.positionCount = 4; lr.widthMultiplier = 0.03f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(1f, 1f, 1f, 0.6f);
        float h = 1.5f; Vector3 z = Vector3.back * 0.6f;
        lr.SetPositions(new[] { pos + new Vector3(-h, -h) + z, pos + new Vector3(-h, h) + z, pos + new Vector3(h, h) + z, pos + new Vector3(h, -h) + z });
        Destroy(fr, 1.5f);
    }

    void PlayEffect(Vector3 pos, Color c)
    {
        if (popEffect == null) return;
        var go = new GameObject("pop");
        go.transform.position = pos + Vector3.back * 0.5f;
        go.transform.localScale = Vector3.one * effectScale;
        var vfx = go.AddComponent<VisualEffect>();
        vfx.visualEffectAsset = popEffect;
        if (vfx.HasVector4(colorProperty)) vfx.SetVector4(colorProperty, c);
        vfx.Play();
        Destroy(go, 3f);
    }

    // ---------- анимации ----------
    IEnumerator PopAndDestroy(Transform t)
    {
        Vector3 s0 = t.localScale;
        for (float e = 0; e < popTime; e += Time.deltaTime)
        {
            if (t == null) yield break;
            float k = e / popTime;
            t.localScale = s0 * (k < 0.3f ? 1f + k : 1.3f * (1f - (k - 0.3f) / 0.7f));
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    // появление бомбы: выпрыгивает с перелётом по размеру
    IEnumerator PopIn(Tile tile)
    {
        const float time = 0.22f;
        for (float e = 0; e < time; e += Time.deltaTime)
        {
            if (tile.t == null) yield break;
            float k = e / time;
            float s = k < 0.6f ? Mathf.Lerp(0f, 1.25f, k / 0.6f) : Mathf.Lerp(1.25f, 1f, (k - 0.6f) / 0.4f);
            tile.t.localScale = Vector3.one * tile.s * s;
            yield return null;
        }
        if (tile.t != null) tile.t.localScale = Vector3.one * tile.s;
    }

    IEnumerator Shake()
    {
        for (float e = 0; e < shakeTime; e += Time.deltaTime)
        {
            float k = 1f - e / shakeTime;
            cam.transform.position = camBase + (Vector3)(Random.insideUnitCircle * shakeAmount * k);
            yield return null;
        }
        cam.transform.position = camBase;
    }

    IEnumerator Tween(Transform[] ts, Vector3[] to, float time)
    {
        var from = new Vector3[ts.Length];
        for (int i = 0; i < ts.Length; i++) from[i] = ts[i].position;
        for (float e = 0; e < time; e += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, e / time);
            for (int i = 0; i < ts.Length; i++) ts[i].position = Vector3.Lerp(from[i], to[i], k);
            yield return null;
        }
        for (int i = 0; i < ts.Length; i++) ts[i].position = to[i];
    }

    IEnumerator Fall(List<Transform> ts, List<Vector3> to)
    {
        var from = new Vector3[ts.Count]; var dur = new float[ts.Count]; float max = 0;
        for (int i = 0; i < ts.Count; i++)
        {
            from[i] = ts[i].position;
            dur[i] = Mathf.Max(0.12f, Vector3.Distance(from[i], to[i]) / fallSpeed);
            max = Mathf.Max(max, dur[i]);
        }
        for (float e = 0; e < max + 0.1f; e += Time.deltaTime)
        {
            for (int i = 0; i < ts.Count; i++)
            {
                float k = Mathf.Clamp01(e / dur[i]);
                float eased = k * k;                                   // ускорение при падении
                Vector3 p = Vector3.Lerp(from[i], to[i], eased);
                float land = Mathf.Clamp01((e - dur[i]) / 0.1f);       // лёгкий отскок при приземлении
                if (k >= 1f && land < 1f) p.y += Mathf.Sin(land * Mathf.PI) * 0.08f;
                ts[i].position = p;
            }
            yield return null;
        }
        for (int i = 0; i < ts.Count; i++) ts[i].position = to[i];
    }
}
