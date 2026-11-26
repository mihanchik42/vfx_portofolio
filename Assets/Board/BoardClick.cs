using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

// Клик по фишке: фишка исчезает, на её месте играет эффект в цвет фишки.
// Пробел — вернуть все фишки.
public class BoardClick : MonoBehaviour
{
    public VisualEffectAsset effect;
    public string colorProperty = "New Color";
    public float effectScale = 1f;

    Camera cam;

    void Start() => cam = Camera.main;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            foreach (Transform t in transform) t.gameObject.SetActive(true);

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        Vector3 p = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        foreach (Transform t in transform)
        {
            if (!t.gameObject.activeSelf) continue;
            if (Mathf.Abs(p.x - t.position.x) > 0.5f || Mathf.Abs(p.y - t.position.y) > 0.5f) continue;

            Color c = t.GetComponent<Renderer>().sharedMaterial.color;
            Pop(t.position, c);
            t.gameObject.SetActive(false);
            break;
        }
    }

    void Pop(Vector3 pos, Color c)
    {
        var go = new GameObject("pop");
        go.transform.position = pos + Vector3.back * 0.5f;
        go.transform.localScale = Vector3.one * effectScale;
        var vfx = go.AddComponent<VisualEffect>();
        vfx.visualEffectAsset = effect;
        if (vfx.HasVector4(colorProperty)) vfx.SetVector4(colorProperty, c);
        vfx.Play();
        Destroy(go, 3f);
    }
}
