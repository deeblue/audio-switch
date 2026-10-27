namespace AudioSwitch.Presentation;

/// <summary>Where the panel opens, in device-independent pixels.</summary>
public sealed record PopupBounds(double Left, double Top, double Width, double Height);

/// <summary>Finds where to open the panel next to the notification area.</summary>
public interface ITrayAnchor
{
    PopupBounds PlacePopup(double width, double height);
}
