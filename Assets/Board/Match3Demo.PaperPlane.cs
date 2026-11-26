using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Match3Demo
{
    static readonly Color PlaneFlame = new Color(1f, 0.56f, 0.12f);
    static readonly Color PlaneWhite = new Color(1f, 0.97f, 0.79f);
    const float PlaneScale = 0.82f;

    Material planeMaterial;
    Material jetMaterial;
    Sprite planeSprite;

    void OnDestroy()
    {
        if (planeSprite != null) Destroy(planeSprite);
        if (rocketSprite != null) Destroy(rocketSprite);
        if (rocketHalfSprite != null) Destroy(rocketHalfSprite);
        if (jetMaterial != null) Destroy(jetMaterial);
        if (planeMaterial != null) Destroy(planeMaterial);
    }

    void PlaceStarterPlane()
    {
        var cell = new Vector2Int(width / 2, height / 2);
        Destroy(grid[cell.x, cell.y].t.gameObject);
        grid[cell.x, cell.y] = SpawnPlane(cell);
    }

    void SetupPlaneSquare()
    {
        if (width < 4 || height < 3 || tileMaterials.Length < 2) return;
        Select(null);
        pressed = false;
        ReplaceWithDemoTile(1, 1, 0);
        ReplaceWithDemoTile(1, 2, 0);
        ReplaceWithDemoTile(2, 1, 0);
        ReplaceWithDemoTile(2, 2, 1);
        ReplaceWithDemoTile(3, 2, 0);
    }

    void ReplaceWithDemoTile(int x, int y, int color)
    {
        Destroy(grid[x, y].t.gameObject);
        grid[x, y] = Spawn(color, CellPos(x, y));
    }

    List<Vector2Int[]> FindPlaneSquares()
    {
        var squares = new List<Vector2Int[]>();
        for (int y = 0; y < height - 1; y++)
            for (int x = 0; x < width - 1; x++)
            {
                int color = ColorAt(x, y);
                if (color < 0 || ColorAt(x + 1, y) != color ||
                    ColorAt(x, y + 1) != color || ColorAt(x + 1, y + 1) != color)
                    continue;

                squares.Add(new[] {
                    new Vector2Int(x, y), new Vector2Int(x + 1, y),
                    new Vector2Int(x, y + 1), new Vector2Int(x + 1, y + 1)
                });
            }
        return squares;
    }

    static HashSet<Vector2Int> PlanPlaneSpawns(List<Vector2Int[]> squares,
        HashSet<Vector2Int> matched, HashSet<Vector2Int> bombs, Vector2Int? a, Vector2Int? b)
    {
        var planes = new HashSet<Vector2Int>();
        var occupied = new HashSet<Vector2Int>();
        foreach (var square in squares)
        {
            if (System.Array.Exists(square, occupied.Contains)) continue;
            Vector2Int? spawn = null;
            foreach (var cell in square)
            {
                if (bombs.Contains(cell)) continue;
                if (!spawn.HasValue || cell == a || cell == b) spawn = cell;
            }
            if (!spawn.HasValue) continue;
            occupied.UnionWith(square);
            matched.UnionWith(square);
            planes.Add(spawn.Value);
        }
        return planes;
    }

    Tile SpawnPlane(Vector2Int cell)
    {
        var model = CreatePlaneModel(CellPos(cell) + Vector3.back * 0.05f);
        model.transform.SetParent(transform, true);
        model.transform.rotation = Quaternion.Euler(0f, 0f, 29f);
        model.transform.localScale = Vector3.one * PlaneScale;
        return new Tile { t = model.transform, color = -1, plane = true, s = PlaneScale };
    }

    GameObject CreatePlaneModel(Vector3 position)
    {
        if (planeMaterial == null) planeMaterial = new Material(Shader.Find("Sprites/Default"));
        if (planeSprite == null)
        {
            var texture = Resources.Load<Texture2D>("PaperPlane/PlaneBody");
            planeSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), texture.width / 1.18f, 0, SpriteMeshType.FullRect);
        }
        var model = new GameObject("Paper Plane");
        model.transform.position = position;
        var renderer = model.AddComponent<SpriteRenderer>();
        renderer.sprite = planeSprite;
        renderer.sortingOrder = 11;
        return model;
    }

    bool TryLaunchPaperPlane(Vector2Int? target)
    {
        if (busy || !target.HasValue || grid[target.Value.x, target.Value.y].plane) return false;
        Select(null);
        pressed = false;
        var start = new Vector2Int(target.Value.x < width / 2 ? width - 1 : 0, 0);
        StartCoroutine(PreviewPlaneFlight(CellPos(start), target.Value));
        return true;
    }

    IEnumerator ActivatePlane(Vector2Int origin)
    {
        yield return ResolveBoosterChain(new Queue<Vector2Int>(new[] { origin }));
    }

    IEnumerator LaunchPlane(Vector2Int origin, Queue<Vector2Int> pending)
    {
        HitBoosterCell(origin + Vector2Int.up, pending);
        HitBoosterCell(origin + Vector2Int.down, pending);
        HitBoosterCell(origin + Vector2Int.left, pending);
        HitBoosterCell(origin + Vector2Int.right, pending);
        var target = PickPlaneTarget(origin);
        if (target.HasValue)
            yield return FlyPaperPlane(CellPos(origin), target.Value, pending);
    }

    IEnumerator PreviewPlaneFlight(Vector3 origin, Vector2Int target)
    {
        busy = true;
        var pending = new Queue<Vector2Int>();
        yield return FlyPaperPlane(origin, target, pending);
        yield return ResolveBoosterChain(pending, false);
        busy = false;
    }

    Vector2Int? PickPlaneTarget(Vector2Int origin)
    {
        Vector2Int? target = null;
        int bestScore = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var tile = grid[x, y];
                if (tile == null || tile.plane || tile.rocketAxis != Vector2Int.zero) continue;
                int score = Mathf.Abs(x - origin.x) + Mathf.Abs(y - origin.y);
                if (tile.bomb) score += width + height;
                if (score <= bestScore) continue;
                bestScore = score;
                target = new Vector2Int(x, y);
            }
        return target;
    }

    IEnumerator FlyPaperPlane(Vector3 origin, Vector2Int cell, Queue<Vector2Int> pending)
    {
        var start = origin + Vector3.back * 0.3f;
        var target = CellPos(cell) + Vector3.back * 0.3f;
        float arc = Mathf.Abs(start.x - target.x) < 1f ? 1.15f : 0.55f;
        float lift = target.y > (CellPos(0, 0).y + CellPos(0, height - 1).y) * 0.5f ? -1.4f : 1.4f;
        var control = (start + target) * 0.5f + new Vector3(arc, lift, 0f);
        var plane = CreatePlaneModel(start);
        plane.transform.localScale = Vector3.one * PlaneScale;
        var flame = CreateAfterburn(plane.transform, -0.45f);
        const float flightTime = 0.61f;
        var launchDirection = (control - start).normalized;
        StartCoroutine(ShakePlaneLaunch(launchDirection));

        for (float elapsed = 0f; elapsed < flightTime; elapsed += Time.deltaTime)
        {
            float progress = elapsed / flightTime;
            float t = progress * progress * (2f - progress);
            var a = Vector3.Lerp(start, control, t);
            var b = Vector3.Lerp(control, target, t);
            plane.transform.SetPositionAndRotation(Vector3.Lerp(a, b, t),
                Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg));
            flame.widthMultiplier = 1f + 0.18f * Mathf.Sin(elapsed * 105f);
            yield return null;
        }

        plane.transform.position = target;
        plane.GetComponent<SpriteRenderer>().enabled = false;
        foreach (var trail in plane.GetComponentsInChildren<TrailRenderer>())
            trail.emitting = false;
        Destroy(plane, 0.29f);
        StartCoroutine(ShowPaperPlaneImpact(target, planeMaterial));

        HitBoosterCell(cell, pending);
    }

    TrailRenderer AddJetTrail(Transform parent, string label, Color color, float width, float lifetime, int order)
    {
        var nozzle = new GameObject(label);
        nozzle.transform.SetParent(parent, false);
        nozzle.transform.localPosition = new Vector3(-0.38f, 0f, 0.01f);
        var trail = nozzle.AddComponent<TrailRenderer>();
        if (jetMaterial == null)
        {
            jetMaterial = new Material(Shader.Find("Sprites/Default"));
            jetMaterial.mainTexture = Resources.Load<Texture2D>("PaperPlane/JetProfile");
        }
        trail.sharedMaterial = jetMaterial;
        trail.sortingOrder = order;
        trail.time = lifetime;
        trail.minVertexDistance = 0.022f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, width * 0.55f),
            new Keyframe(0.18f, width), new Keyframe(1f, 0f));
        trail.startColor = color;
        trail.endColor = new Color(color.r, color.g * 0.4f, color.b * 0.25f, 0f);
        return trail;
    }

    IEnumerator ShakePlaneLaunch(Vector3 heading)
    {
        const float duration = 0.13f;
        const float amplitude = 0.065f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            float kick = Mathf.Cos(t * Mathf.PI * 2.5f) * (1f - t) * (1f - t);
            cam.transform.position = camBase - heading * (amplitude * kick);
            yield return null;
        }
        cam.transform.position = camBase;
    }

    IEnumerator ShowPaperPlaneImpact(Vector3 center, Material material)
    {
        var root = new GameObject("Paper Plane Impact");
        var rays = new LineRenderer[8];
        for (int i = 0; i < rays.Length; i++)
        {
            var ray = new GameObject("Impact streak").AddComponent<LineRenderer>();
            ray.transform.SetParent(root.transform);
            ray.sharedMaterial = material;
            ray.sortingOrder = 12;
            ray.positionCount = 2;
            rays[i] = ray;
        }

        const float duration = 0.22f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            float expansion = 1f - (1f - t) * (1f - t);
            for (int i = 0; i < rays.Length; i++)
            {
                float angle = (i + 0.5f) * Mathf.PI * 2f / rays.Length;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                float distance = Mathf.Lerp(0.12f, i % 2 == 0 ? 1.0f : 0.72f, expansion);
                var ray = rays[i];
                ray.startWidth = 0.085f * (1f - t);
                ray.endWidth = 0f;
                ray.startColor = new Color(PlaneWhite.r, PlaneWhite.g, PlaneWhite.b, 1f - t);
                ray.endColor = new Color(PlaneFlame.r, PlaneFlame.g, PlaneFlame.b, 0.6f * (1f - t));
                ray.SetPosition(0, center + direction * distance);
                ray.SetPosition(1, center + direction * (distance + Mathf.Lerp(0.34f, 0.06f, t)));
            }
            yield return null;
        }

        Destroy(root);

    }
}

