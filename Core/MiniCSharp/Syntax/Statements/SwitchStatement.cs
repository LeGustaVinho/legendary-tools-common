using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class SwitchStatement : Statement
    {
        private readonly Expression _target;
        private readonly List<SwitchSection> _sections;

        public SwitchStatement(Expression target, List<SwitchSection> sections)
        {
            _target = target;
            _sections = sections;
        }

        public override void Execute(ScriptContext context)
        {
            object switchValue = _target.Evaluate(context).Value;
            SwitchSection section = FindMatchingSection(context, switchValue);

            if (section == null)
            {
                return;
            }

            try
            {
                section.Execute(context);
            }
            catch (ScriptBreakException)
            {
            }
        }

        public override System.Collections.IEnumerator ExecuteCoroutine(ScriptContext context)
        {
            object switchValue = _target.Evaluate(context).Value;
            SwitchSection section = FindMatchingSection(context, switchValue);

            if (section == null)
            {
                yield break;
            }

            System.Collections.IEnumerator enumerator = section.ExecuteCoroutine(context);

            while (true)
            {
                object yieldedValue;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    yieldedValue = enumerator.Current;
                }
                catch (ScriptBreakException)
                {
                    yield break;
                }

                yield return yieldedValue;
            }
        }

        private SwitchSection FindMatchingSection(ScriptContext context, object switchValue)
        {
            SwitchSection defaultSection = null;

            for (int sectionIndex = 0; sectionIndex < _sections.Count; sectionIndex++)
            {
                SwitchSection section = _sections[sectionIndex];

                for (int labelIndex = 0; labelIndex < section.Labels.Count; labelIndex++)
                {
                    SwitchLabel label = section.Labels[labelIndex];

                    if (label.IsDefault)
                    {
                        defaultSection ??= section;
                        continue;
                    }

                    object caseValue = label.Expression.Evaluate(context).Value;

                    if (RuntimeOperators.AreEqual(switchValue, caseValue))
                    {
                        return section;
                    }
                }
            }

            return defaultSection;
        }
    }

    internal sealed class SwitchSection
    {
        private readonly List<Statement> _statements;

        public SwitchSection(List<SwitchLabel> labels, List<Statement> statements)
        {
            Labels = labels;
            _statements = statements;
        }

        public List<SwitchLabel> Labels { get; }

        public void Execute(ScriptContext context)
        {
            for (int index = 0; index < _statements.Count; index++)
            {
                _statements[index].Execute(context);
            }
        }

        public System.Collections.IEnumerator ExecuteCoroutine(ScriptContext context)
        {
            for (int index = 0; index < _statements.Count; index++)
            {
                System.Collections.IEnumerator enumerator = _statements[index].ExecuteCoroutine(context);

                while (enumerator.MoveNext())
                {
                    yield return enumerator.Current;
                }
            }
        }
    }

    internal sealed class SwitchLabel
    {
        private SwitchLabel(Expression expression, bool isDefault)
        {
            Expression = expression;
            IsDefault = isDefault;
        }

        public Expression Expression { get; }

        public bool IsDefault { get; }

        public static SwitchLabel ForCase(Expression expression)
        {
            return new SwitchLabel(expression, false);
        }

        public static SwitchLabel ForDefault()
        {
            return new SwitchLabel(null, true);
        }
    }
}
