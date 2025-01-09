using System;
using UnityEngine;

namespace LegendaryTools.Commander
{
        /// <summary>
        /// Common interface for all command classes.
        /// </summary>
        public interface ICommand
        {
            /// <summary>
            /// Executes the command on the given receiver.
            /// </summary>
            /// <param name="receiver">The receiver that will execute the command.</param>
            void Execute(object receiver);
        }
}