using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Text;

namespace WinUIShared.Helpers
{
    public static class Utility
    {
        public static DependencyObject? FindChildElementByName(this DependencyObject tree, string sName)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(tree); i++)
            {
                var child = VisualTreeHelper.GetChild(tree, i);
                if (child != null && ((FrameworkElement)child).Name == sName)
                    return child;
                var childInSubtree = FindChildElementByName(child, sName);
                if (childInSubtree != null)
                    return childInSubtree;
            }
            return null;
        }
    }
}
