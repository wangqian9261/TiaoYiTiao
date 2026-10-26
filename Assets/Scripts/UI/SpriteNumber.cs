using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Decimal sprites share a 160x192 canvas/baseline; Images are pooled.</summary>
public sealed class SpriteNumber : MonoBehaviour
{
    public Sprite[] digits = new Sprite[10];
    public float height = 60f;
    public bool rightAligned;
    private readonly List<Image> _images = new List<Image>();
    private int _value = -1;
    public int Value => _value;
    public void SetNumber(int value)
    {
        value = Mathf.Max(0, value);
        if (_value == value) return;
        _value = value;
        string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float advance = height * 160f / 192f;
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(advance * text.Length, height); root.pivot = new Vector2(rightAligned ? 1f : 0f, .5f);
        for (int i = 0; i < text.Length; i++)
        {
            if (i >= _images.Count)
            {
                var go = new GameObject("Digit" + i, typeof(RectTransform), typeof(Image)); go.transform.SetParent(transform, false);
                var image = go.GetComponent<Image>(); image.raycastTarget = false; _images.Add(image);
            }
            var item = _images[i]; item.gameObject.SetActive(true); item.sprite = digits[text[i] - '0'];
            var rect = item.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
            rect.pivot = new Vector2(0,.5f); rect.sizeDelta = new Vector2(advance,height); rect.anchoredPosition = new Vector2(i * advance, 0);
        }
        for (int i = text.Length; i < _images.Count; i++) _images[i].gameObject.SetActive(false);
    }
}
