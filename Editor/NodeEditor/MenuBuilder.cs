#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// Concrete menu builder used by both context menus and toolbar dropdowns.
/// </summary>
public class MenuBuilder : IMenuBuilder
{
    private readonly GenericMenu _menu = new();

    public void AddItem(string path, Action onClick, bool enabled = true, bool isChecked = false)
    {
        if (!enabled)
        {
            _menu.AddDisabledItem(new UnityEngine.GUIContent(path), isChecked);
            return;
        }

        _menu.AddItem(new UnityEngine.GUIContent(path), isChecked, () => onClick?.Invoke());
    }

    public void AddSeparator(string pathPrefix = "")
    {
        _menu.AddSeparator(string.IsNullOrEmpty(pathPrefix) ? "" : pathPrefix);
    }

    public void AddItemIf(string path, Func<bool> isVisible, Action onClick, Func<bool> isEnabled = null,
        Func<bool> isChecked = null)
    {
        if (isVisible == null || !isVisible()) return;
        bool enabled = isEnabled == null || isEnabled();
        bool checkedState = isChecked != null && isChecked();
        AddItem(path, onClick, enabled, checkedState);
    }

    public void AddItemDynamic(string path, Action onClick, Func<bool> isVisible, Func<bool> isEnabled,
        Func<bool> isChecked)
    {
        if (isVisible == null || !isVisible()) return;
        AddItem(path, onClick, isEnabled == null || isEnabled(), isChecked != null && isChecked());
    }

    /// <summary>Shows the menu as a context popup at the current mouse position.</summary>
    public void Show()
    {
        _menu.ShowAsContext();
    }

    /// <summary>Exposes ShowAsContext for toolbar dropdowns.</summary>
    public void ShowAsContext()
    {
        _menu.ShowAsContext();
    }
}
#endif