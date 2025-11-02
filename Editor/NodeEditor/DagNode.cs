using System;
using UnityEngine;

/// <summary>
/// Concrete DAG node stored as a sub-asset of <see cref="DagGraph"/>.
/// Implements <see cref="IDagNode"/> to satisfy editor and algorithm needs.
/// </summary>
[Serializable]
public class DagNode : ScriptableObject, IDagNode
{
    [SerializeField] private int id;
    [SerializeField] private string title = "Node";
    [SerializeField] private Vector2 position;

    // Example serialized fields (some drawn inline in the node via [ShowInNode])
    [ShowInNode, SerializeField] public string note;
    [ShowInNode, SerializeField] public int someInt;
    [SerializeField] public float someFloat;
    [ShowInNode, SerializeField] public UnityEngine.Object reference;

    // Optional appearance overrides consumed by the editor
    [Header("Appearance (Optional)")]
    [SerializeField] private bool overrideSize = false;
    [SerializeField] private Vector2 customSize = new Vector2(200f, 110f);

    [SerializeField] private bool overrideStyles = false;
    [SerializeField] private GUISkin styleSkin = null;

    // Default style names match built-in editor styles when available
    [SerializeField] private string normalStyleName = "flow node 0";
    [SerializeField] private string selectedStyleName = "flow node 0 on";

    /// <summary>Gets the unique node identifier.</summary>
    public int Id => id;

    /// <summary>Gets the display title.</summary>
    public string Title => title;

    /// <summary>Gets the logical canvas position.</summary>
    public Vector2 Position => position;

    /// <summary>Gets a value indicating whether a custom size is in use.</summary>
    public bool HasCustomNodeSize => overrideSize;

    /// <summary>Gets the custom size.</summary>
    public Vector2 NodeSize => customSize;

    /// <summary>Gets a value indicating whether custom GUI styles are in use.</summary>
    public bool HasCustomNodeStyles => overrideStyles;

    /// <summary>Gets the unselected style name.</summary>
    public string NormalStyleName => normalStyleName;

    /// <summary>Gets the selected style name.</summary>
    public string SelectedStyleName => selectedStyleName;

    /// <summary>Gets the GUISkin where styles are resolved first.</summary>
    public GUISkin StyleSkin => styleSkin;

    /// <summary>Sets the identifier. Internal to the editor workflow.</summary>
    internal void SetId(int value) => id = value;

    /// <summary>Sets the display title. Internal to the editor workflow.</summary>
    internal void SetTitle(string value) => title = value;

    /// <summary>Sets the canvas position. Internal to the editor workflow.</summary>
    internal void SetPosition(Vector2 value) => position = value;
}
