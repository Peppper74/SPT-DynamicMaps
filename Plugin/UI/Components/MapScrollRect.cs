namespace DynamicMaps.UI.Components;

using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MapScrollRect : ScrollRect
{
    public MapScrollRect(System.IntPtr ptr) : base(ptr) { }

    public System.Action OnBeginDragCallback;

    public override void OnBeginDrag(PointerEventData eventData)
    {
        base.OnBeginDrag(eventData);
        OnBeginDragCallback?.Invoke();
    }
}
