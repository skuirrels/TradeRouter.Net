using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TradeRouter.Common;
using TradeRouter.Graph;
using TradeRouter.Locations;
using TradeRouter.Ports;

namespace TradeRouter.Data;

/// <summary>
/// Helper to load and deserialize embedded compressed datasets into in-memory data structures.
/// </summary>
public static class EmbeddedResources
{
    private static readonly Assembly CurrentAssembly = typeof(EmbeddedResources).Assembly;

    /// <summary>
    /// Loads and builds the MaritimeGraph from the embedded marnet.json.gz dataset.
    /// </summary>
    public static MaritimeGraph LoadMaritimeGraph()
    {
        var graph = new MaritimeGraph();

        using var rawStream = CurrentAssembly.GetManifestResourceStream("TradeRouter.Data.marnet.json.gz")
            ?? throw new InvalidOperationException("Embedded resource 'TradeRouter.Data.marnet.json.gz' not found.");

        using var gzipStream = new GZipStream(rawStream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzipStream);

        var root = doc.RootElement;
        if (root.TryGetProperty("nodes", out var nodesElement) && nodesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var nodeItem in nodesElement.EnumerateArray())
            {
                double lon = nodeItem[0].GetDouble();
                double lat = nodeItem[1].GetDouble();
                graph.AddNode(new Coordinate(lon, lat));
            }
        }

        if (root.TryGetProperty("edges", out var edgesElement) && edgesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var edgeItem in edgesElement.EnumerateArray())
            {
                int u = edgeItem[0].GetInt32();
                int v = edgeItem[1].GetInt32();
                double weight = edgeItem[2].GetDouble();
                string? passage = edgeItem[3].ValueKind == JsonValueKind.String ? edgeItem[3].GetString() : null;

                graph.AddDirectedEdge(u, v, weight, passage);
            }
        }

        graph.BuildIndex();
        return graph;
    }

    /// <summary>
    /// Loads the UN/LOCODE list from the embedded unlocode.json.gz dataset: arrays of
    /// [code, name, longitude or null, latitude or null, function flags].
    /// </summary>
    public static UnLocodeDatabase LoadUnLocodes()
    {
        using var rawStream = CurrentAssembly.GetManifestResourceStream("TradeRouter.Data.unlocode.json.gz")
            ?? throw new InvalidOperationException("Embedded resource 'TradeRouter.Data.unlocode.json.gz' not found.");

        using var gzipStream = new GZipStream(rawStream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzipStream);

        var supplement = LoadUnLocodeSupplement();
        var confirmedSeaPorts = LoadSeaPortSupplement();
        var entries = new List<UnLocode>(110_000);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string code = item[0].GetString() ?? "";
            string name = item[1].GetString() ?? "";
            Coordinate? coordinate = item[2].ValueKind == JsonValueKind.Number && item[3].ValueKind == JsonValueKind.Number
                ? new Coordinate(item[2].GetDouble(), item[3].GetDouble())
                : null;
            var functions = (LocationFunctions)item[4].GetInt32();
            // Codes UNECE lists without the sea-port function but that are documented deep-sea terminals.
            if (confirmedSeaPorts.Contains(code))
                functions |= LocationFunctions.SeaPort;
            string coordinateSource = coordinate.HasValue ? "UNECE" : "";

            // The supplement only fills codes UNECE publishes without coordinates; it never overrides UNECE.
            if (!coordinate.HasValue && supplement.TryGetValue(code, out var extra))
            {
                coordinate = extra.Coordinate;
                coordinateSource = extra.Source;
            }

            entries.Add(new UnLocode(code, name, coordinate, functions, coordinateSource));
        }

        return new UnLocodeDatabase(entries);
    }

    /// <summary>
    /// Loads unlocode-supplement.json: researched coordinates for codes that UNECE publishes without any,
    /// each with the source it was taken from.
    /// </summary>
    private static Dictionary<string, (Coordinate Coordinate, string Source)> LoadUnLocodeSupplement()
    {
        var result = new Dictionary<string, (Coordinate, string)>(StringComparer.OrdinalIgnoreCase);
        using var stream = CurrentAssembly.GetManifestResourceStream("TradeRouter.Data.unlocode-supplement.json");
        if (stream == null)
            return result;

        using var doc = JsonDocument.Parse(stream);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string code = item.GetProperty("code").GetString() ?? "";
            var coordinate = new Coordinate(item.GetProperty("lon").GetDouble(), item.GetProperty("lat").GetDouble());
            string source = item.GetProperty("source").GetString() ?? "supplement";
            result[code] = (coordinate, source);
        }
        return result;
    }

    /// <summary>
    /// Loads unlocode-seaport-supplement.json: codes that UNECE publishes without the sea-port function but
    /// that cited sources document as sea ports, so movements may declare them as ports and route sea legs.
    /// </summary>
    private static HashSet<string> LoadSeaPortSupplement()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var stream = CurrentAssembly.GetManifestResourceStream("TradeRouter.Data.unlocode-seaport-supplement.json");
        if (stream == null)
            return result;

        using var doc = JsonDocument.Parse(stream);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string code = item.GetProperty("code").GetString() ?? "";
            if (code.Length > 0)
                result.Add(code);
        }
        return result;
    }

    /// <summary>
    /// Loads port-code-aliases.json: official UN/LOCODE sea-port codes mapped to the differently coded record that
    /// the embedded port list holds for the same port, reviewed from name and position.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadPortCodeAliases()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = CurrentAssembly.GetManifestResourceStream("TradeRouter.Data.port-code-aliases.json");
        if (stream == null)
            return result;

        using var doc = JsonDocument.Parse(stream);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string code = item.GetProperty("code").GetString() ?? "";
            string port = item.GetProperty("port").GetString() ?? "";
            if (code.Length > 0 && port.Length > 0)
                result[code] = port;
        }
        return result;
    }

    /// <summary>
    /// Loads and builds the PortDatabase from the embedded ports.json.gz dataset.
    /// </summary>
    public static PortDatabase LoadPortDatabase()
    {
        var ports = new List<Port>();

        using var rawStream = CurrentAssembly.GetManifestResourceStream("TradeRouter.Data.ports.json.gz")
            ?? throw new InvalidOperationException("Embedded resource 'TradeRouter.Data.ports.json.gz' not found.");

        using var gzipStream = new GZipStream(rawStream, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gzipStream);

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            return new PortDatabase(ports);

        foreach (var portElem in root.EnumerateArray())
        {
            string portCode = portElem.TryGetProperty("port", out var p) ? p.GetString() ?? "" : "";
            string name = portElem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string cty = portElem.TryGetProperty("cty", out var c) ? c.GetString() ?? "" : "";
            double t = portElem.TryGetProperty("t", out var tv) ? tv.GetDouble() : 0.0;
            double x = portElem.TryGetProperty("x", out var xv) ? xv.GetDouble() : 0.0;
            double y = portElem.TryGetProperty("y", out var yv) ? yv.GetDouble() : 0.0;

            var toCountries = new List<string>();
            if (portElem.TryGetProperty("to_cty", out var toArr) && toArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in toArr.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrEmpty(s)) toCountries.Add(s);
                }
            }

            ports.Add(new Port
            {
                PortCode = portCode,
                Name = name,
                Country = cty,
                TerminalFlag = t,
                ToCountries = toCountries,
                Coordinate = new Coordinate(x, y)
            });
        }

        return new PortDatabase(ports);
    }
}
