using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WinUIShared.Controls
{
    //This ComboBox extension is used to fix the issue where the ComboBox shifts when opened/closed
    public class ComboBoxEx : ComboBox
    {
        double _cachedWidth;

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
                _cachedWidth = baseSize.Width;

            return baseSize;
        }
    }
}
