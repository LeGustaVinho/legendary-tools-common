using System;

namespace LegendaryTools.MiniCSharp
{
    /// <summary>
    /// Executes a small C#-like scripting language using a reflection-friendly runtime.
    /// </summary>
    public sealed class MiniCSharpInterpreter
    {
        private readonly ScriptContext _context = new ScriptContext();

        /// <summary>
        /// Gets the access policy used by the interpreter.
        /// The default behavior is allow-all with blacklist rules.
        /// </summary>
        public TypeAccessPolicy AccessPolicy
        {
            get { return _context.AccessPolicy; }
        }

        /// <summary>
        /// Gets or sets whether types are automatically resolved from all loaded AppDomain assemblies.
        /// </summary>
        public bool AutoResolveTypesFromAppDomain
        {
            get { return _context.AutoResolveTypesFromAppDomain; }
            set { _context.AutoResolveTypesFromAppDomain = value; }
        }

        /// <summary>
        /// Registers or replaces a global variable.
        /// </summary>
        public void SetVariable<T>(string name, T value)
        {
            _context.SetOrDefineGlobal(name, typeof(T), value);
        }

        /// <summary>
        /// Registers an object instance so scripts can read/write members and call public methods.
        /// </summary>
        public void RegisterObject(string name, object instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            _context.SetOrDefineGlobal(name, instance.GetType(), instance);
        }

        /// <summary>
        /// Registers a type using its CLR type name as the script alias.
        /// Registration is optional when automatic AppDomain type resolution is enabled.
        /// </summary>
        public void RegisterType<T>()
        {
            _context.RegisterType(typeof(T).Name, typeof(T));
        }

        /// <summary>
        /// Registers an additional type alias for declarations, constructors, and static member access.
        /// Registration is optional when automatic AppDomain type resolution is enabled.
        /// </summary>
        public void RegisterType<T>(string alias)
        {
            _context.RegisterType(alias, typeof(T));
        }

        /// <summary>
        /// Registers an additional type alias for declarations, constructors, and static member access.
        /// Registration is optional when automatic AppDomain type resolution is enabled.
        /// </summary>
        public void RegisterType(string alias, Type type)
        {
            _context.RegisterType(alias, type);
        }

        /// <summary>
        /// Adds a type to the blacklist.
        /// </summary>
        public void BlacklistType<T>()
        {
            _context.AccessPolicy.BlacklistType<T>();
        }

        /// <summary>
        /// Adds a type to the blacklist.
        /// </summary>
        public void BlacklistType(Type type)
        {
            _context.AccessPolicy.BlacklistType(type);
        }

        /// <summary>
        /// Adds a type name to the blacklist.
        /// Matches both simple name and full name.
        /// </summary>
        public void BlacklistTypeName(string typeName)
        {
            _context.AccessPolicy.BlacklistTypeName(typeName);
        }

        /// <summary>
        /// Adds a namespace prefix to the blacklist.
        /// Example: "System.IO" blocks System.IO and its child namespaces.
        /// </summary>
        public void BlacklistNamespace(string namespacePrefix)
        {
            _context.AccessPolicy.BlacklistNamespace(namespacePrefix);
        }

        /// <summary>
        /// Adds an assembly name to the blacklist.
        /// Example: "System.IO.FileSystem".
        /// </summary>
        public void BlacklistAssembly(string assemblyName)
        {
            _context.AccessPolicy.BlacklistAssembly(assemblyName);
        }

        /// <summary>
        /// Reads a global variable value.
        /// </summary>
        public T GetVariable<T>(string name)
        {
            object value = _context.GetVariable(name).Value;
            return (T)RuntimeConversion.ConvertTo(value, typeof(T));
        }

        /// <summary>
        /// Compiles a script from source text.
        /// The returned script can be executed repeatedly without lexing or parsing again.
        /// </summary>
        public RuntimeScript Compile(string code)
        {
            if (code == null)
            {
                throw new ArgumentNullException(nameof(code));
            }

            var tokens = new Lexer(code).ScanTokens();
            var program = new Parser(tokens, _context.ResolveType).ParseProgram();

            return new RuntimeScript(code, program);
        }

        /// <summary>
        /// Tries to compile a script from source text without throwing script syntax errors.
        /// This is useful for editor validation before play mode.
        /// </summary>
        public bool TryCompile(string code, out RuntimeScript script, out string errorMessage)
        {
            script = null;
            errorMessage = null;

            try
            {
                script = Compile(code);
                return true;
            }
            catch (ScriptException exception)
            {
                errorMessage = exception.Message;
                return false;
            }
            catch (ArgumentException exception)
            {
                errorMessage = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Executes a previously compiled script.
        /// </summary>
        public void Execute(RuntimeScript script)
        {
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }

            script.Execute(_context);
        }

        /// <summary>
        /// Executes a script from source text.
        /// This method keeps the old behavior and compiles the script every time it is called.
        /// Use Compile followed by Execute(RuntimeScript) when the same script must run multiple times.
        /// </summary>
        public void Execute(string code)
        {
            Execute(Compile(code));
        }
    }
}
