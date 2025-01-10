using System;
using System.Threading.Tasks;
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

        /// <summary>
        /// Undoes the command on the given receiver.
        /// </summary>
        /// <param name="receiver">The receiver that will undo the command.</param>
        void Unexecute(object receiver);
    }
    
    /// <summary>
    /// Interface para comandos que suportam execução assíncrona.
    /// </summary>
    public interface IAsyncCommand
    {
        /// <summary>
        /// Executa o comando de forma assíncrona no receptor fornecido.
        /// </summary>
        /// <param name="receiver">O receptor que executará o comando.</param>
        /// <returns>Uma tarefa que representa a operação assíncrona.</returns>
        Task ExecuteAsync(object receiver);
    }
}