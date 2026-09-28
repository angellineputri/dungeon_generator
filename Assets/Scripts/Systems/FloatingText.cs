using System.Collections;
using UnityEngine;
using TMPro;

public class FloatingText : MonoBehaviour
{
    const float Duration = 1.1f;
    const float RiseDistance = 1.2f;
    const float FontSize = 6f;
    const int SortingOrder = 60;

    static TMP_FontAsset cachedFont;

    public static void Show(string message, Vector3 worldPosition, Color color)
    {
        GameObject go = new GameObject("FloatingText");
        go.transform.position = worldPosition + Vector3.up * 0.6f
                              + Vector3.right * Random.Range(-0.2f, 0.2f);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = message;
        tmp.color = color;
        tmp.fontSize = FontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(6f, 2f);

        TMP_FontAsset font = ResolveFont();
        if (font != null) tmp.font = font;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = SortingOrder;

        go.AddComponent<FloatingText>().StartCoroutine(Animate(go.transform, tmp, color));
    }

    static TMP_FontAsset ResolveFont()
    {
        if (cachedFont != null) return cachedFont;
        cachedFont = TMP_Settings.defaultFontAsset;
        if (cachedFont == null)
            cachedFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return cachedFont;
    }

    static IEnumerator Animate(Transform t, TextMeshPro tmp, Color color)
    {
        Vector3 start = t.position;
        Vector3 end = start + Vector3.up * RiseDistance;
        float elapsed = 0f;
        while (elapsed < Duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / Duration);
            t.position = Vector3.Lerp(start, end, k);
            if (tmp != null)
            {
                Color c = color;
                c.a = Mathf.Lerp(1f, 0f, k);
                tmp.color = c;
            }
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }
}
