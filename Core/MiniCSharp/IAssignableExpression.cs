namespace LegendaryTools.MiniCSharp
{
    internal interface IAssignableExpression
    {
        RuntimeValue GetValue(ScriptContext context);

        void Assign(ScriptContext context, object value);
    }
}