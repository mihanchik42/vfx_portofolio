using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Match3Demo
{
    const float RocketSpeed = 17f;
    const float RocketExitDistance = 1.15f;
    const float RocketWidth = 0.9f;
    Sprite rocketSprite;
    Sprite rocketHalfSprite;

    static Dictionary<Vector2Int, Vector2Int> PlanRocketSpawns(
        List<List<Vector2Int>> runs, HashSet<Vector2Int> bombSpawns, Vector2Int? a, Vector2Int? b)
    {
        var rockets = new Dictionary<Vector2Int, Vector2Int>();
        foreach (var run in runs)
        {
            if (run.Count < 4 || run.Exists(bombSpawns.Contains)) continue;
            var cell = run[run.Count / 2];
            if (b.HasValue && run.Contains(b.Value)) cell = b.Value;
            else if (a.HasValue && run.Contains(a.Value)) cell = a.Value;
            rockets[cell] = run[1] - run[0];
        }
        return rockets;
    }

    void PlaceRecordingRocket(Vector2Int? position, Vector2Int axis)
    {
        if (!position.HasValue) return;
        var cell = position.Value;
        Select(null);
        pressed = false;
        Destroy(grid[cell.x, cell.y].t.gameObject);
        var rocket = SpawnRocket(cell, axis);
        grid[cell.x, cell.y] = rocket;
        StartCoroutine(PopIn(rocket));
    }

    Tile SpawnRocket(Vector2Int cell, Vector2Int axis)
    {
        EnsureRocketSprites();
        var body = CreateRocketVisual(transform, axis, rocketSprite, "Line Rocket");
        body.position = CellPos(cell) + Vector3.back * 0.05f;
        return new Tile { t = body, color = -1, rocketAxis = axis, s = 1f };
    }

    void EnsureRocketSprites()
    {
        if (rocketSprite != null) return;
        var texture = Resources.Load<Texture2D>("Rocket/RocketBody");
        float pixelsPerUnit = texture.width / RocketWidth;
        var pivot = new Vector2(0.5f, 0.5f);
        float halfWidth = texture.width * 0.5f;
        rocketSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
        rocketHalfSprite = Sprite.Create(texture, new Rect(halfWidth, 0, halfWidth, texture.height),
            pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
    }

    static Transform CreateRocketVisual(Transform parent, Vector2 direction, Sprite sprite, string name)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localRotation = Quaternion.Euler(0, 0,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        var renderer = part.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 11;
        return part.transform;
    }

    IEnumerator ActivateRocket(Vector2Int origin)
    {
        yield return ResolveBoosterChain(new Queue<Vector2Int>(new[] { origin }));
    }

    IEnumerator LaunchRocket(Vector2Int cell, Vector2Int axis, Queue<Vector2Int> pending)
    {
        var position = CellPos(cell);
        var center = position + Vector3.back * 0.3f;
        var backward = LaunchRocketHalf(center, -axis, out var backwardFlame);
        var forward = LaunchRocketHalf(center, axis, out var forwardFlame);
        int negativeCells = CountRocketCells(cell, -axis);
        int positiveCells = CountRocketCells(cell, axis);
        int reach = Mathf.Max(negativeCells, positiveCells);
        float limit = reach + RocketExitDistance;
        int nextCell = 1;
        float distance = 0f;
        float elapsed = 0f;

        while (distance < limit)
        {
            elapsed += Time.deltaTime;
            float speed = RocketSpeed * Mathf.Lerp(0.58f, 1f, Mathf.Clamp01(elapsed / 0.085f));
            distance = Mathf.Min(limit, distance + speed * Time.deltaTime);
            if (backward != null)
            {
                backwardFlame.widthMultiplier = 1f + 0.18f * Mathf.Sin(elapsed * 105f);
                MoveRocketHalf(ref backward, center, -axis, distance, negativeCells);
            }
            if (forward != null)
            {
                forwardFlame.widthMultiplier = 1f + 0.18f * Mathf.Sin(elapsed * 98f + 1f);
                MoveRocketHalf(ref forward, center, axis, distance, positiveCells);
            }

            while (nextCell <= distance && nextCell <= reach)
            {
                HitBoosterCell(cell - axis * nextCell, pending);
                HitBoosterCell(cell + axis * nextCell, pending);
                nextCell++;
            }
            yield return null;
        }
    }

    Transform LaunchRocketHalf(Vector3 center, Vector2Int direction, out TrailRenderer flame)
    {
        var half = CreateRocketVisual(transform, direction, rocketHalfSprite, "Rocket Half");
        half.position = center + (Vector3)(Vector2)direction * (RocketWidth * 0.25f);

        flame = CreateAfterburn(half, -RocketWidth * 0.25f);
        return half;
    }

    TrailRenderer CreateAfterburn(Transform emitter, float nozzleOffset)
    {
        var smoke = AddJetTrail(emitter, "Rocket Smoke", new Color(0.7f, 0.63f, 0.62f, 0.36f),
            0.36f, 0.17f, 8);
        smoke.widthCurve = new AnimationCurve(new Keyframe(0f, 0.08f),
            new Keyframe(0.55f, 0.35f), new Keyframe(1f, 0.42f));

        var flame = AddJetTrail(emitter, "Rocket Flame", new Color(1f, 0.66f, 0.14f),
            0.29f, 0.085f, 9);
        flame.endColor = new Color(1f, 0.12f, 0.025f, 0f);
        flame.widthCurve = new AnimationCurve(new Keyframe(0f, 0.16f),
            new Keyframe(0.22f, 0.29f), new Keyframe(0.63f, 0.12f), new Keyframe(1f, 0f));

        var core = AddJetTrail(emitter, "Rocket Core", new Color(1f, 0.98f, 0.77f),
            0.13f, 0.046f, 10);
        core.endColor = new Color(1f, 0.36f, 0.05f, 0f);
        foreach (var trail in new[] { smoke, flame, core })
            trail.transform.localPosition = new Vector3(nozzleOffset, 0f, 0.01f);
        return flame;
    }

    int CountRocketCells(Vector2Int cell, Vector2Int direction)
    {
        int count = 0;
        for (var next = cell + direction; InBounds(next); next += direction)
            count++;
        return count;
    }

    void MoveRocketHalf(ref Transform half, Vector3 center, Vector2Int direction, float travel, int cells)
    {
        float end = cells + RocketExitDistance;
        half.position = center + (Vector3)(Vector2)direction * (Mathf.Min(travel, end) + RocketWidth * 0.25f);
        if (travel < end) return;

        half.GetComponent<SpriteRenderer>().enabled = false;
        foreach (var trail in half.GetComponentsInChildren<TrailRenderer>())
            trail.emitting = false;
        Destroy(half.gameObject, 0.21f);
        half = null;
    }
}
