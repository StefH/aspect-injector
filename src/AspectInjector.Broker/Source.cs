using System;
using System.ComponentModel;
using System.Reflection;

namespace AspectInjector.Broker
{
    /// <summary>
    /// Advice argument sources enumeration.
    /// </summary>
    public enum Source : byte
    {
        /// <summary>
        /// Target's instance or <c>null</c> if target is static.
        /// Should be of type <see cref="object" />.
        /// </summary>
        Instance = 1,

        /// <summary>
        /// Target's owner. Usually class or struct type.
        /// Should be of type <see cref="System.Type" />.
        /// </summary>
        Type = 2,

        /// <summary>
        /// Target method.-
        /// Should be of type <see cref="MethodBase" />.
        /// </summary>
        [Obsolete("Use Source.Metadata instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        Method = 3,

        /// <summary>
        /// Target method metadata.
        /// Should be of type <see cref="MethodBase" />.
        /// </summary>
        Metadata = 3,

        /// <summary>
        /// Target method delegate. Usage <example>Target(<see cref="Arguments"/>)</example> for chaining methods.
        /// Should be of type <see cref="T:Func{object[],object}" />.
        /// Works only with <see cref="Kind.Around" />.
        /// </summary>
        Target = 4,

        /// <summary>
        /// Target name.
        /// Should be of type <see cref="string" />.
        /// </summary>
        Name = 5,

        /// <summary>
        /// Target method arguments.
        /// Should be of type <see cref="object"/>[].
        /// </summary>
        Arguments = 6,

        /// <summary>
        /// Target method result.
        /// Should be of type <see cref="object" />.
        /// Works only with <see cref="Kind.After" />.
        /// </summary>
        ReturnValue = 7,

        /// <summary>
        /// Target method result type.
        /// Should be of type <see cref="System.Type" />.
        /// </summary>
        ReturnType = 8,

        /// <summary>
        /// Set of injections that trigger this advice.
        /// Should be of type <see cref="Attribute" />[].
        /// </summary>
        [Obsolete("Use Source.Triggers instead")]   
        [EditorBrowsable(EditorBrowsableState.Never)]
        Injections = 9,

        /// <summary>
        /// Set of injections that trigger this advice.
        /// Should be of type <see cref="Attribute" />[].
        /// </summary>
        Triggers = 9
    }
}
