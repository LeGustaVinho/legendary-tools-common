namespace LegendaryTools.MiniCSharp
{
    internal abstract class Expression
    {
        public abstract RuntimeValue Evaluate(ScriptContext context);
    }
}