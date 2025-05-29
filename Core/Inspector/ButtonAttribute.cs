using System;

namespace LegendaryTools.Inspector
{
    public class ButtonAttribute : Attribute
    {
        /// <summary>Use this to override the label on the button.</summary>
        public string Name;

        /// <summary>
        /// If the button contains parameters, you can disable the foldout it creates by setting this to true.
        /// </summary>
        public bool Expanded;

        /// <summary>
        /// <para>Whether to display the button method's parameters (if any) as values in the inspector. True by default.</para>
        /// <para>If this is set to false, the button method will instead be invoked through an ActionResolver or ValueResolver (based on whether it returns a value), giving access to contextual named parameter values like "InspectorProperty property" that can be passed to the button method.</para>
        /// </summary>
        public bool DisplayParameters = true;

        private int buttonHeight;

        /// <summary>
        /// Gets the height of the button. If it's zero or below then use default.
        /// </summary>
        public int ButtonHeight
        {
            get => buttonHeight;
            set
            {
                buttonHeight = value;
                HasDefinedButtonHeight = true;
            }
        }
        
        public bool HasDefinedButtonHeight { get; private set; }

        /// <summary>
        /// Creates a button in the inspector named after the method.
        /// </summary>
        public ButtonAttribute()
        {
            Name = (string)null;
        }

        /// <summary>
        /// Creates a button in the inspector named after the method.
        /// </summary>
        /// <param name="buttonSize">The size of the button.</param>
        public ButtonAttribute(int buttonSize)
        {
            ButtonHeight = buttonSize;
            Name = (string)null;
        }

        /// <summary>Creates a button in the inspector with a custom name.</summary>
        /// <param name="name">Custom name for the button.</param>
        public ButtonAttribute(string name)
        {
            Name = name;
        }

        /// <summary>Creates a button in the inspector with a custom name.</summary>
        /// <param name="name">Custom name for the button.</param>
        /// <param name="buttonSize">Size of the button in pixels.</param>
        public ButtonAttribute(string name, int buttonSize)
        {
            Name = name;
            ButtonHeight = buttonSize;
        }
    }
}
