using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinUIShared.Helpers;

namespace WinUIShared.Controls
{
    //This ComboBox extension is used to fix the issue where the ComboBox shifts when opened/closed
    public class ComboBoxEx : ComboBox
    {
        double _cachedWidth;
        private bool cornerRadiusSet;

        protected override void OnDropDownOpened(object e)
        {
            Width = _cachedWidth;

            base.OnDropDownOpened(e);
        }

        protected override void OnDropDownClosed(object e)
        {
            Width = double.NaN;

            base.OnDropDownClosed(e);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var baseSize = base.MeasureOverride(availableSize);

            if (baseSize.Width != 64)
            {
                _cachedWidth = baseSize.Width;
            }

            if (!cornerRadiusSet)
            {
                if(CornerRadius == default) CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"];
                var popup = this.FindChildElementByName("Popup") as Popup;
                if (popup == null) return baseSize;
                var popupBorder = popup.Child.FindChildElementByName("PopupBorder") as Border;
                if(popupBorder != null)
                {
                    popupBorder.CornerRadius = CornerRadius;
                    cornerRadiusSet = true;
                }
            }

            return baseSize;
        }
    }
}
