namespace AudioSwitch.Presentation;

public sealed record PopupBounds(double Left, double Top, double Width, double Height);

public interface ITrayAnchor
{
    PopupBounds PlacePopup(double width, double height);
}
