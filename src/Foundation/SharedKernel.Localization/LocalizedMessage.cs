using System.Globalization;
using SharedKernel.Localization.Internal;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Localization;

/// <summary>
/// A message with a stable code, a default text, and no placeholders. Also the entry point for
/// defining messages that take typed arguments: see the <c>Define</c> overloads.
/// </summary>
/// <remarks>
/// <para>
/// Declare each message once, as a <see langword="static readonly"/> field, and build errors and
/// texts from it. The compiler then checks every call site against the argument count and types,
/// and a translation is looked up by the same code the error carries:
/// </para>
/// <code>
/// public static class OrderMessages
/// {
///     public static readonly LocalizedMessage&lt;Guid&gt; NotFound = LocalizedMessage.Define&lt;Guid&gt;(
///         "order.not_found", "Order {orderId} was not found.", "orderId");
/// }
///
/// return OrderMessages.NotFound.ToError(ErrorType.NotFound, orderId);
/// </code>
/// <para>
/// <c>Define</c> validates the definition when the field is initialized: the argument names must
/// match the placeholders in the default text exactly, and each placeholder's format must suit its
/// argument's type. A mistake throws <see cref="ArgumentException"/> from the type's static
/// initializer, so any test that touches the type finds it.
/// </para>
/// <para>
/// Argument names are given explicitly rather than taken from the order of the placeholders, so
/// rewording the default text can never silently swap two values of the same type.
/// </para>
/// </remarks>
public sealed class LocalizedMessage
{
    private readonly MessageDefinition _definition;

    private LocalizedMessage(MessageDefinition definition) => _definition = definition;

    /// <summary>Gets the stable message code, which is also the <c>Error.Code</c> of errors built from this message.</summary>
    public string Code => _definition.Code;

    /// <summary>Gets the default text, used when no translation exists and for <c>Error.Message</c>.</summary>
    public MessageTemplate DefaultTemplate => _definition.DefaultTemplate;

    /// <summary>Defines a message without placeholders.</summary>
    /// <param name="code">The stable message code, for example <c>"order.cannot_cancel"</c>.</param>
    /// <param name="defaultText">The default text. Must not contain placeholders.</param>
    /// <returns>The message definition.</returns>
    /// <exception cref="ArgumentException">The code or text is blank, the text is not a valid template, or it contains placeholders.</exception>
    public static LocalizedMessage Define(string code, string defaultText)
        => new(new MessageDefinition(code, defaultText, [], []));

    /// <summary>Defines a message with one typed argument.</summary>
    /// <typeparam name="T1">The type of the argument.</typeparam>
    /// <param name="code">The stable message code, for example <c>"order.not_found"</c>.</param>
    /// <param name="defaultTemplate">The default text, for example <c>"Order {orderId} was not found."</c>.</param>
    /// <param name="argument1">The placeholder name that receives the argument, for example <c>"orderId"</c>.</param>
    /// <returns>The message definition.</returns>
    /// <exception cref="ArgumentException">
    /// The code or text is blank, the text is not a valid template, the argument name does not
    /// match the text's placeholders, or a placeholder's format does not suit the argument type.
    /// </exception>
    public static LocalizedMessage<T1> Define<T1>(string code, string defaultTemplate, string argument1)
        => new(new MessageDefinition(code, defaultTemplate, [argument1], [default(T1)]));

    /// <summary>Defines a message with two typed arguments.</summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <param name="code">The stable message code.</param>
    /// <param name="defaultTemplate">The default text, for example <c>"Transfer of {amount:N2} from {account} failed."</c>.</param>
    /// <param name="argument1">The placeholder name that receives the first argument.</param>
    /// <param name="argument2">The placeholder name that receives the second argument.</param>
    /// <returns>The message definition.</returns>
    /// <exception cref="ArgumentException">
    /// The code or text is blank, the text is not a valid template, the argument names do not
    /// match the text's placeholders, or a placeholder's format does not suit its argument type.
    /// </exception>
    public static LocalizedMessage<T1, T2> Define<T1, T2>(
        string code, string defaultTemplate, string argument1, string argument2)
        => new(new MessageDefinition(code, defaultTemplate, [argument1, argument2], [default(T1), default(T2)]));

    /// <summary>Defines a message with three typed arguments.</summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <param name="code">The stable message code.</param>
    /// <param name="defaultTemplate">The default text.</param>
    /// <param name="argument1">The placeholder name that receives the first argument.</param>
    /// <param name="argument2">The placeholder name that receives the second argument.</param>
    /// <param name="argument3">The placeholder name that receives the third argument.</param>
    /// <returns>The message definition.</returns>
    /// <exception cref="ArgumentException">
    /// The code or text is blank, the text is not a valid template, the argument names do not
    /// match the text's placeholders, or a placeholder's format does not suit its argument type.
    /// </exception>
    public static LocalizedMessage<T1, T2, T3> Define<T1, T2, T3>(
        string code, string defaultTemplate, string argument1, string argument2, string argument3)
        => new(new MessageDefinition(
            code, defaultTemplate, [argument1, argument2, argument3], [default(T1), default(T2), default(T3)]));

    /// <summary>Defines a message with four typed arguments.</summary>
    /// <typeparam name="T1">The type of the first argument.</typeparam>
    /// <typeparam name="T2">The type of the second argument.</typeparam>
    /// <typeparam name="T3">The type of the third argument.</typeparam>
    /// <typeparam name="T4">The type of the fourth argument.</typeparam>
    /// <param name="code">The stable message code.</param>
    /// <param name="defaultTemplate">The default text.</param>
    /// <param name="argument1">The placeholder name that receives the first argument.</param>
    /// <param name="argument2">The placeholder name that receives the second argument.</param>
    /// <param name="argument3">The placeholder name that receives the third argument.</param>
    /// <param name="argument4">The placeholder name that receives the fourth argument.</param>
    /// <returns>The message definition.</returns>
    /// <exception cref="ArgumentException">
    /// The code or text is blank, the text is not a valid template, the argument names do not
    /// match the text's placeholders, or a placeholder's format does not suit its argument type.
    /// </exception>
    /// <remarks>A message that needs more than four values is usually two messages.</remarks>
    public static LocalizedMessage<T1, T2, T3, T4> Define<T1, T2, T3, T4>(
        string code, string defaultTemplate, string argument1, string argument2, string argument3, string argument4)
        => new(new MessageDefinition(
            code,
            defaultTemplate,
            [argument1, argument2, argument3, argument4],
            [default(T1), default(T2), default(T3), default(T4)]));

    /// <summary>Creates an error with this message's code and default text.</summary>
    /// <param name="type">The error type, which decides the HTTP status. Must not be <see cref="ErrorType.None"/>.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is <see cref="ErrorType.None"/> or not a defined value.</exception>
    public Error ToError(ErrorType type) => _definition.ToError(type, []);

    /// <summary>Returns this message in <paramref name="culture"/>, or the default text when <paramref name="catalog"/> has no translation.</summary>
    /// <param name="catalog">The catalog to look the translation up in, or <see langword="null"/> for the default text.</param>
    /// <param name="culture">The culture to translate into.</param>
    /// <returns>The translated or default text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public string Format(ILocalizationCatalog? catalog, CultureInfo culture) => _definition.Format(catalog, culture, []);

    /// <inheritdoc />
    public override string ToString() => Code;
}

/// <summary>A message with a stable code, a default text, and one typed argument. Create it with <see cref="LocalizedMessage.Define{T1}"/>.</summary>
/// <typeparam name="T1">The type of the argument.</typeparam>
public sealed class LocalizedMessage<T1>
{
    private readonly MessageDefinition _definition;

    internal LocalizedMessage(MessageDefinition definition) => _definition = definition;

    /// <summary>Gets the stable message code, which is also the <c>Error.Code</c> of errors built from this message.</summary>
    public string Code => _definition.Code;

    /// <summary>Gets the default text, used when no translation exists and for <c>Error.Message</c>.</summary>
    public MessageTemplate DefaultTemplate => _definition.DefaultTemplate;

    /// <summary>
    /// Creates an error whose <c>Message</c> is the default text filled with the argument (formatted
    /// with the invariant culture) and whose <c>MessageArguments</c> carries the argument, so the
    /// HTTP boundary can fill a translation with it.
    /// </summary>
    /// <param name="type">The error type, which decides the HTTP status. Must not be <see cref="ErrorType.None"/>.</param>
    /// <param name="arg1">The argument.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is <see cref="ErrorType.None"/> or not a defined value.</exception>
    public Error ToError(ErrorType type, T1 arg1) => _definition.ToError(type, [arg1]);

    /// <summary>
    /// Returns this message in <paramref name="culture"/> filled with the argument, or the default
    /// text when <paramref name="catalog"/> has no usable translation.
    /// </summary>
    /// <param name="catalog">The catalog to look the translation up in, or <see langword="null"/> for the default text.</param>
    /// <param name="culture">The culture to translate into; also used to format the argument.</param>
    /// <param name="arg1">The argument.</param>
    /// <returns>The translated or default text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public string Format(ILocalizationCatalog? catalog, CultureInfo culture, T1 arg1)
        => _definition.Format(catalog, culture, [arg1]);

    /// <inheritdoc />
    public override string ToString() => Code;
}

/// <summary>A message with a stable code, a default text, and two typed arguments. Create it with <see cref="LocalizedMessage.Define{T1, T2}"/>.</summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
public sealed class LocalizedMessage<T1, T2>
{
    private readonly MessageDefinition _definition;

    internal LocalizedMessage(MessageDefinition definition) => _definition = definition;

    /// <summary>Gets the stable message code, which is also the <c>Error.Code</c> of errors built from this message.</summary>
    public string Code => _definition.Code;

    /// <summary>Gets the default text, used when no translation exists and for <c>Error.Message</c>.</summary>
    public MessageTemplate DefaultTemplate => _definition.DefaultTemplate;

    /// <summary>Creates an error carrying the arguments. See <see cref="LocalizedMessage{T1}.ToError"/>.</summary>
    /// <param name="type">The error type. Must not be <see cref="ErrorType.None"/>.</param>
    /// <param name="arg1">The first argument.</param>
    /// <param name="arg2">The second argument.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is <see cref="ErrorType.None"/> or not a defined value.</exception>
    public Error ToError(ErrorType type, T1 arg1, T2 arg2) => _definition.ToError(type, [arg1, arg2]);

    /// <summary>Returns this message in <paramref name="culture"/> filled with the arguments, or the default text.</summary>
    /// <param name="catalog">The catalog to look the translation up in, or <see langword="null"/> for the default text.</param>
    /// <param name="culture">The culture to translate into; also used to format the arguments.</param>
    /// <param name="arg1">The first argument.</param>
    /// <param name="arg2">The second argument.</param>
    /// <returns>The translated or default text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public string Format(ILocalizationCatalog? catalog, CultureInfo culture, T1 arg1, T2 arg2)
        => _definition.Format(catalog, culture, [arg1, arg2]);

    /// <inheritdoc />
    public override string ToString() => Code;
}

/// <summary>A message with a stable code, a default text, and three typed arguments. Create it with <see cref="LocalizedMessage.Define{T1, T2, T3}"/>.</summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
public sealed class LocalizedMessage<T1, T2, T3>
{
    private readonly MessageDefinition _definition;

    internal LocalizedMessage(MessageDefinition definition) => _definition = definition;

    /// <summary>Gets the stable message code, which is also the <c>Error.Code</c> of errors built from this message.</summary>
    public string Code => _definition.Code;

    /// <summary>Gets the default text, used when no translation exists and for <c>Error.Message</c>.</summary>
    public MessageTemplate DefaultTemplate => _definition.DefaultTemplate;

    /// <summary>Creates an error carrying the arguments. See <see cref="LocalizedMessage{T1}.ToError"/>.</summary>
    /// <param name="type">The error type. Must not be <see cref="ErrorType.None"/>.</param>
    /// <param name="arg1">The first argument.</param>
    /// <param name="arg2">The second argument.</param>
    /// <param name="arg3">The third argument.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is <see cref="ErrorType.None"/> or not a defined value.</exception>
    public Error ToError(ErrorType type, T1 arg1, T2 arg2, T3 arg3) => _definition.ToError(type, [arg1, arg2, arg3]);

    /// <summary>Returns this message in <paramref name="culture"/> filled with the arguments, or the default text.</summary>
    /// <param name="catalog">The catalog to look the translation up in, or <see langword="null"/> for the default text.</param>
    /// <param name="culture">The culture to translate into; also used to format the arguments.</param>
    /// <param name="arg1">The first argument.</param>
    /// <param name="arg2">The second argument.</param>
    /// <param name="arg3">The third argument.</param>
    /// <returns>The translated or default text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public string Format(ILocalizationCatalog? catalog, CultureInfo culture, T1 arg1, T2 arg2, T3 arg3)
        => _definition.Format(catalog, culture, [arg1, arg2, arg3]);

    /// <inheritdoc />
    public override string ToString() => Code;
}

/// <summary>A message with a stable code, a default text, and four typed arguments. Create it with <see cref="LocalizedMessage.Define{T1, T2, T3, T4}"/>.</summary>
/// <typeparam name="T1">The type of the first argument.</typeparam>
/// <typeparam name="T2">The type of the second argument.</typeparam>
/// <typeparam name="T3">The type of the third argument.</typeparam>
/// <typeparam name="T4">The type of the fourth argument.</typeparam>
public sealed class LocalizedMessage<T1, T2, T3, T4>
{
    private readonly MessageDefinition _definition;

    internal LocalizedMessage(MessageDefinition definition) => _definition = definition;

    /// <summary>Gets the stable message code, which is also the <c>Error.Code</c> of errors built from this message.</summary>
    public string Code => _definition.Code;

    /// <summary>Gets the default text, used when no translation exists and for <c>Error.Message</c>.</summary>
    public MessageTemplate DefaultTemplate => _definition.DefaultTemplate;

    /// <summary>Creates an error carrying the arguments. See <see cref="LocalizedMessage{T1}.ToError"/>.</summary>
    /// <param name="type">The error type. Must not be <see cref="ErrorType.None"/>.</param>
    /// <param name="arg1">The first argument.</param>
    /// <param name="arg2">The second argument.</param>
    /// <param name="arg3">The third argument.</param>
    /// <param name="arg4">The fourth argument.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is <see cref="ErrorType.None"/> or not a defined value.</exception>
    public Error ToError(ErrorType type, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        => _definition.ToError(type, [arg1, arg2, arg3, arg4]);

    /// <summary>Returns this message in <paramref name="culture"/> filled with the arguments, or the default text.</summary>
    /// <param name="catalog">The catalog to look the translation up in, or <see langword="null"/> for the default text.</param>
    /// <param name="culture">The culture to translate into; also used to format the arguments.</param>
    /// <param name="arg1">The first argument.</param>
    /// <param name="arg2">The second argument.</param>
    /// <param name="arg3">The third argument.</param>
    /// <param name="arg4">The fourth argument.</param>
    /// <returns>The translated or default text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public string Format(ILocalizationCatalog? catalog, CultureInfo culture, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        => _definition.Format(catalog, culture, [arg1, arg2, arg3, arg4]);

    /// <inheritdoc />
    public override string ToString() => Code;
}
