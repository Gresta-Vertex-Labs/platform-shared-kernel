using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using SharedKernel.Localization.Internal;

namespace SharedKernel.Localization;

/// <summary>
/// Collects translations from code and JSON files, validates every one of them, and builds an
/// immutable <see cref="InMemoryLocalizationCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>JSON format.</b> One file per culture, holding an object whose values are message
/// templates. Nested objects are joined with dots, so both files below define
/// <c>order.not_found</c>:
/// </para>
/// <code>
/// { "order.not_found": "{orderId} numaralı sipariş bulunamadı." }
///
/// { "order": { "not_found": "{orderId} numaralı sipariş bulunamadı." } }
/// </code>
/// <para>
/// Comments and trailing commas are allowed. A value that is not a string or an object, a code
/// defined twice in one file, and a template with a syntax error all throw
/// <see cref="FormatException"/> naming the file and the code, so a broken translation stops the
/// service at startup instead of reaching a user.
/// </para>
/// <para>
/// <b>Later wins.</b> When the same code and culture are added twice from different sources, the
/// later one replaces the earlier. Add shared translations first and service-specific overrides
/// after them.
/// </para>
/// <para>
/// <b>Culture names.</b> File names must be culture names (<c>tr.json</c>, <c>de-DE.json</c>).
/// Cultures come from the operating system's ICU data, so an app published with
/// <c>InvariantGlobalization</c> enabled has no culture except the invariant one and cannot load
/// them.
/// </para>
/// </remarks>
public sealed class LocalizationCatalogBuilder
{
    private readonly Dictionary<(string Code, string Culture), MessageTemplate> _templates = [];
    private readonly Dictionary<string, CultureInfo> _cultures = new(StringComparer.Ordinal);

    /// <summary>Adds or replaces the translation of <paramref name="code"/> in <paramref name="culture"/>.</summary>
    /// <param name="code">The message code, for example <c>"order.not_found"</c>.</param>
    /// <param name="culture">The culture of the translation. Use <see cref="CultureInfo.InvariantCulture"/> for a default that every culture falls back to.</param>
    /// <param name="template">The translated template, for example <c>"{orderId} numaralı sipariş bulunamadı."</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="code"/> or <paramref name="template"/> is null, empty, or whitespace-only.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="template"/> is not a valid <see cref="MessageTemplate"/>.</exception>
    public LocalizationCatalogBuilder Add(string code, CultureInfo culture, string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        Store(code, culture, MessageTemplate.Parse(template));
        return this;
    }

    /// <summary>Adds the translations in a UTF-8 JSON stream for one culture.</summary>
    /// <param name="utf8Json">The JSON content. Read to the end and left open.</param>
    /// <param name="culture">The culture of every translation in the stream.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="FormatException">The JSON or one of its templates is not valid.</exception>
    public LocalizationCatalogBuilder AddJson(Stream utf8Json, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ArgumentNullException.ThrowIfNull(culture);

        AddJsonCore(utf8Json, culture, $"the JSON stream for culture '{culture.Name}'");
        return this;
    }

    /// <summary>Adds the translations in a JSON file whose name is its culture, such as <c>tr.json</c>.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank, or the file name is not a culture name.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">The JSON or one of its templates is not valid.</exception>
    public LocalizationCatalogBuilder AddJsonFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return AddJsonFile(path, CultureFromName(Path.GetFileNameWithoutExtension(path), path));
    }

    /// <summary>Adds the translations in a JSON file for one culture.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="culture">The culture of every translation in the file.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FormatException">The JSON or one of its templates is not valid.</exception>
    public LocalizationCatalogBuilder AddJsonFile(string path, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(culture);

        using FileStream stream = File.OpenRead(path);
        AddJsonCore(stream, culture, path);
        return this;
    }

    /// <summary>
    /// Adds every <c>*.json</c> file directly inside <paramref name="directory"/>, each named after
    /// its culture (<c>en.json</c>, <c>tr.json</c>, <c>de-DE.json</c>). Subdirectories are not read.
    /// </summary>
    /// <param name="directory">The directory, for example <c>Path.Combine(AppContext.BaseDirectory, "Localization")</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is blank, contains no JSON files, or a file name is not a culture name.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="FormatException">A file or one of its templates is not valid.</exception>
    /// <remarks>Files are read in ordinal order of their names, so the result does not depend on the file system.</remarks>
    public LocalizationCatalogBuilder AddJsonDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"The localization directory '{directory}' does not exist.");
        }

        string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            throw new ArgumentException($"The localization directory '{directory}' contains no .json files.", nameof(directory));
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            AddJsonFile(file);
        }

        return this;
    }

    /// <summary>
    /// Adds every JSON file embedded in <paramref name="assembly"/> whose resource name is
    /// <paramref name="resourcePrefix"/> followed by a culture name and <c>.json</c>.
    /// </summary>
    /// <param name="assembly">The assembly that embeds the files, typically <c>typeof(SomeTypeInIt).Assembly</c>.</param>
    /// <param name="resourcePrefix">
    /// The resource-name prefix, ending with a dot. For files in a project folder
    /// <c>Localization</c> of a project whose root namespace is <c>Orders.Api</c>, it is
    /// <c>"Orders.Api.Localization."</c>, and <c>tr.json</c> is found as
    /// <c>Orders.Api.Localization.tr.json</c>.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resourcePrefix"/> is blank, no resource matches it, or a name after the
    /// prefix is not a culture name.
    /// </exception>
    /// <exception cref="FormatException">A file or one of its templates is not valid.</exception>
    /// <remarks>
    /// Embed the files with <c>&lt;EmbeddedResource Include="Localization\*.json" /&gt;</c>. This is
    /// the way to ship translations inside a library, because it does not depend on files being
    /// copied next to the application.
    /// </remarks>
    public LocalizationCatalogBuilder AddEmbeddedJson(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);

        string[] names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !name.AsSpan(resourcePrefix.Length, name.Length - resourcePrefix.Length - ".json".Length).Contains('.'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (names.Length == 0)
        {
            throw new ArgumentException(
                $"The assembly '{assembly.GetName().Name}' embeds no resource named '{resourcePrefix}<culture>.json'. "
                    + "Check the prefix against the assembly's root namespace and folder, and that the files are EmbeddedResource items.",
                nameof(resourcePrefix));
        }

        foreach (string name in names)
        {
            string cultureName = name[resourcePrefix.Length..^".json".Length];
            CultureInfo culture = CultureFromName(cultureName, name);

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            AddJsonCore(stream, culture, name);
        }

        return this;
    }

    /// <summary>Builds an immutable catalog from everything added so far. The builder can still be used afterwards.</summary>
    /// <returns>The catalog.</returns>
    public InMemoryLocalizationCatalog Build()
    {
        CultureInfo[] cultures = [.. _cultures.Values.OrderBy(culture => culture.Name, StringComparer.Ordinal)];

        return new InMemoryLocalizationCatalog(_templates.ToFrozenDictionary(), cultures);
    }

    private void AddJsonCore(Stream stream, CultureInfo culture, string source)
    {
        foreach ((string code, MessageTemplate template) in JsonTranslationReader.Read(stream, source))
        {
            Store(code, culture, template);
        }
    }

    private void Store(string code, CultureInfo culture, MessageTemplate template)
    {
        _templates[(code, culture.Name)] = template;
        _cultures.TryAdd(culture.Name, culture);
    }

    private static CultureInfo CultureFromName(string name, string source)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                // Reported below with the source name.
            }
        }

        throw new ArgumentException(
            $"'{source}' must be named after a culture, such as 'tr.json' or 'de-DE.json', but '{name}' is not a known culture name. "
                + "An app running with InvariantGlobalization enabled knows no culture except the invariant one.");
    }
}
