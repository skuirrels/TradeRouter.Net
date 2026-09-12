using TradeRouter.Common;
using TradeRouter.Spatial;

namespace TradeRouter.Ports;

/// <summary>
/// Database of world maritime ports, indexed spatially via KD-Tree.
/// </summary>
public sealed class PortDatabase
{
    private readonly List<Port> _ports;
    private readonly Dictionary<string, IReadOnlyList<Port>> _portsByCode;
    private readonly KdTree<Port> _kdTree;

    /// <summary>Total number of ports.</summary>
    public int Count => _ports.Count;

    /// <summary>
    /// Initializes a new instance of <see cref="PortDatabase"/>.
    /// </summary>
    public PortDatabase(IEnumerable<Port> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        _ports = ports.ToList();
        var portsByCode = new Dictionary<string, List<Port>>(StringComparer.OrdinalIgnoreCase);

        var treeItems = new List<(Coordinate Point, Port Value)>(_ports.Count);
        foreach (var port in _ports)
        {
            ArgumentNullException.ThrowIfNull(port);
            port.Coordinate.Validate();
            if (port.ToCountries is null)
                throw new ArgumentException($"Port '{port.PortCode}' has a null destination-country list.", nameof(ports));
            if (!double.IsFinite(port.TerminalFlag))
                throw new ArgumentException($"Port '{port.PortCode}' has a non-finite terminal flag.", nameof(ports));
            treeItems.Add((port.Coordinate, port));
            if (!string.IsNullOrEmpty(port.PortCode))
            {
                if (!portsByCode.TryGetValue(port.PortCode, out var candidates))
                {
                    candidates = [];
                    portsByCode.Add(port.PortCode, candidates);
                }
                candidates.Add(port);
            }
        }
        _portsByCode = portsByCode.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<Port>)pair.Value.AsReadOnly(),
            StringComparer.OrdinalIgnoreCase);
        _kdTree = new KdTree<Port>(treeItems);
    }

    /// <summary>
    /// Gets a port by its UN/LOCODE or port code.
    /// </summary>
    public Port? GetByCode(string portCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portCode);
        var candidates = GetByCodeCandidates(portCode);
        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                $"Port code '{portCode.Trim().ToUpperInvariant()}' is ambiguous ({candidates.Count} records). " +
                "Use GetByCode(code, near) to select the geographically nearest record.")
        };
    }

    /// <summary>Gets every port record for a code. Some upstream port codes are not unique.</summary>
    public IReadOnlyList<Port> GetByCodeCandidates(string portCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portCode);
        return _portsByCode.TryGetValue(portCode.Trim(), out var ports) ? ports : Array.Empty<Port>();
    }

    /// <summary>Gets the record for a code that is geographically nearest to a known coordinate.</summary>
    public Port? GetByCode(string portCode, Coordinate near)
    {
        near.Validate();
        var candidates = GetByCodeCandidates(portCode);
        Port? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            double distance = Haversine.UnitSphereDistanceSquared(near, candidate.Coordinate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// Finds the nearest port to a given geographic coordinate.
    /// </summary>
    public Port? FindNearestPort(Coordinate coordinate)
    {
        var result = _kdTree.Query(coordinate);
        return result?.Value;
    }

    /// <summary>
    /// Queries the port database applying terminal and country filters.
    /// </summary>
    public Port? QueryClosestPort(
        Coordinate point,
        bool onlyTerminals = false,
        string? country = null,
        string? toCountry = null,
        bool strict = true)
    {
        point.Validate();

        // Fast path: when no filters are requested, query the spherical KD-Tree directly in O(log N)
        if (!onlyTerminals && string.IsNullOrWhiteSpace(country) && string.IsNullOrWhiteSpace(toCountry))
        {
            return FindNearestPort(point);
        }

        IEnumerable<Port> candidates = _ports;

        if (onlyTerminals)
        {
            var terminalPorts = candidates.Where(p => p.IsTerminal).ToList();
            if (terminalPorts.Count > 0 || strict)
                candidates = terminalPorts;
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            string normalizedCountry = NormalizeCountry(country);
            var countryPorts = candidates.Where(p =>
                NormalizeCountry(p.Country) == normalizedCountry ||
                (normalizedCountry.Length == 2 && p.PortCode.StartsWith(normalizedCountry, StringComparison.OrdinalIgnoreCase))).ToList();

            if (countryPorts.Count > 0 || strict)
                candidates = countryPorts;
        }

        if (!string.IsNullOrWhiteSpace(toCountry))
        {
            string toCtyUpper = NormalizeCountry(toCountry);
            var toCountryPorts = candidates.Where(p =>
                p.ToCountries.Any(tc => NormalizeCountry(tc) == toCtyUpper)).ToList();

            if (toCountryPorts.Count > 0 || strict)
                candidates = toCountryPorts;
        }

        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            if (strict)
                return null;
            candidateList = _ports;
        }

        // Find closest among candidates using spherical chord distance.
        Port? bestPort = null;
        double bestDistSq = double.PositiveInfinity;

        foreach (var port in candidateList)
        {
            double dSq = Haversine.UnitSphereDistanceSquared(point, port.Coordinate);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestPort = port;
            }
        }

        return bestPort;
    }

    /// <summary>
    /// Selects preferred ports matching area polygons.
    /// </summary>
    public List<Port> GetPreferredPorts(
        Coordinate point,
        IReadOnlyList<AreaFeature> areaFeatures,
        int? top = null,
        bool strictArea = true)
    {
        if (areaFeatures == null || areaFeatures.Count == 0)
            return [];

        // 1. Find smallest containing area feature
        AreaFeature? smallestArea = null;
        double minArea = double.PositiveInfinity;

        foreach (var area in areaFeatures)
        {
            if (area.Contains(point))
            {
                if (area.Area < minArea)
                {
                    minArea = area.Area;
                    smallestArea = area;
                }
            }
        }

        // 2. If not found and strictArea is false, find closest polygon within 2000 km
        if (smallestArea == null && !strictArea)
        {
            const double maxDistanceKm = 2000.0;
            double closestDistance = double.PositiveInfinity;

            foreach (var area in areaFeatures)
            {
                double dist = area.DistanceToPoint(point);
                if (dist <= maxDistanceKm && dist < closestDistance)
                {
                    closestDistance = dist;
                    smallestArea = area;
                }
            }
        }

        if (smallestArea == null || smallestArea.PreferredPorts.Count == 0)
            return [];

        double sumShares = smallestArea.PreferredPorts.Sum(p => p.Share);
        if (!double.IsFinite(sumShares))
            throw new ArgumentException($"Preferred port shares in area '{smallestArea.Name}' overflow their finite range.", nameof(areaFeatures));
        double divisor = Math.Max(sumShares, 1.0);

        var result = new List<Port>();
        foreach (var pref in smallestArea.PreferredPorts)
        {
            double normalizedShare = pref.Share / divisor;

            Port resolvedPort;
            var existing = GetByCode(pref.PortId, point);
            if (existing is not null)
            {
                resolvedPort = existing.WithShare(normalizedShare);
            }
            else
            {
                var customCoord = pref.TryGetCoordinate()
                    ?? throw new ArgumentException(
                        $"Preferred port '{pref.PortId}' in area '{smallestArea.Name}' is not in the port list and has no x/y in its props.");
                resolvedPort = new Port
                {
                    PortCode = pref.PortId,
                    Name = pref.PortId,
                    Coordinate = customCoord,
                    Share = normalizedShare
                };
            }

            result.Add(resolvedPort);
        }

        result.Sort((a, b) => (b.Share ?? 0.0).CompareTo(a.Share ?? 0.0));

        return top.HasValue && top.Value > 0 ? result.Take(top.Value).ToList() : result;
    }

    /// <summary>
    /// Generates the port matrix (combinations of origin port and destination port).
    /// </summary>
    public List<(Port OriginPort, Port DestPort)> GetSelectedPortMatrix(
        Coordinate origin,
        Coordinate destination,
        PortParameters? parameters)
    {
        parameters ??= new PortParameters();

        var areasFrom = parameters.PortsInAreasFrom ?? parameters.PortsInAreas;
        var areasTo = parameters.PortsInAreasTo ?? parameters.PortsInAreas;

        var originPorts = new List<Port>();
        if (areasFrom != null && areasFrom.Count > 0)
        {
            originPorts = GetPreferredPorts(origin, areasFrom, strictArea: parameters.StrictArea);
        }

        if (originPorts.Count == 0)
        {
            string? toCty = parameters.CountryRestricted ? parameters.CountryPod : null;
            var closest = QueryClosestPort(
                origin,
                parameters.OnlyTerminals,
                parameters.CountryPol,
                toCty,
                parameters.Strict);

            if (closest != null)
            {
                originPorts.Add(closest.WithShare(1.0));
            }
        }

        var destPorts = new List<Port>();
        if (areasTo != null && areasTo.Count > 0)
        {
            destPorts = GetPreferredPorts(destination, areasTo, strictArea: parameters.StrictArea);
        }

        if (destPorts.Count == 0)
        {
            var closest = QueryClosestPort(
                destination,
                parameters.OnlyTerminals,
                parameters.CountryPod,
                null,
                parameters.Strict);

            if (closest != null)
            {
                destPorts.Add(closest.WithShare(1.0));
            }
        }

        var matrix = new List<(Port, Port)>();
        foreach (var fromPort in originPorts)
        {
            foreach (var toPort in destPorts)
            {
                matrix.Add((fromPort, toPort));
            }
        }

        return matrix;
    }

    private static string NormalizeCountry(string country)
    {
        var chars = new char[country.Length];
        int length = 0;
        foreach (char c in country)
        {
            if (char.IsLetterOrDigit(c))
                chars[length++] = char.ToUpperInvariant(c);
        }
        return new string(chars, 0, length);
    }
}
