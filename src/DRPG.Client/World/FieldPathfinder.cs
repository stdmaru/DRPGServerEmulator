namespace DRPG.Client.World;

/// <summary>
/// A* pathfinder of the field map (✅ v322 <c>0x00b43200</c> → <c>0x00b430f0</c> → <c>0x00b433d0</c> → <c>0x00b43520</c> /
/// <c>0x00b436e0</c>, docs/cliente/campo.md). Used identically by the player tamer (<c>0x008671c0</c>, <c>0x00b4d290</c>),
/// the partner digimon and the server wild digimon (<c>0x007f4b10</c>).
/// <para>
/// Exact parameters of v322: 8-way neighbourhood (NW, N, NE, E, SE, S, SW, W), uniform step cost <c>g + 1</c>,
/// Manhattan heuristic <c>|dx| + |dy|</c>, depth cap <c>min(2 · manhattan + 1, 25)</c>, and target cell always passable
/// during search (<c>0x00b432d0</c>) so an entity can walk up next to a blocked or occupied target and stop before entering it.
/// </para>
/// </summary>
public sealed class FieldPathfinder(FieldCollision collision) : IStepResolver
{
    /// <summary>✅ <c>0x00b430f0</c>: maximum path depth (<c>0x19 = 25</c>).</summary>
    public const int MaxDepth = 25;

    /// <summary>✅ <c>0x00b433d0</c>: the exact 8-neighbour expansion order (NW, N, NE, E, SE, S, SW, W), as offsets from the cell.</summary>
    private static readonly Cell[] Neighbors =
    [
        new(-1, -1), new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0),
    ];

    private sealed class Node(Cell cell, int g, int h, Node? parent)
    {
        public Cell Cell { get; } = cell;
        public int G { get; set; } = g;
        public int H { get; } = h;
        public int F => G + H;
        public Node? Parent { get; set; } = parent;
        public bool Closed { get; set; }
        public List<Node>? Children { get; set; }
    }

    /// <summary>
    /// ✅ <c>0x007f4b10</c> / <c>0x008671c0</c>: resolves the next A* step toward <paramref name="destination"/>, reporting
    /// <see cref="PathStep.BlockedAtTarget"/> when the next cell is the destination itself and is not free
    /// (<see cref="FieldCollision.IsFree"/>) so callers can stop AND still face that cell.
    /// </summary>
    public PathStep? ResolveStep(Cell from, Cell destination)
    {
        if (!TryFindFirstStep(from, destination, out var next))
            return null;
        bool blockedAtTarget = next == destination && !collision.IsFree(next.X, next.Y);
        return new PathStep(next, blockedAtTarget);
    }

    /// <summary>
    /// ✅ <c>0x00b43200</c> + <c>0x00b35b30</c> final-cell check: the next adjacent cell from <paramref name="from"/> toward
    /// <paramref name="destination"/>, or null when already there, unreachable within <see cref="MaxDepth"/>, or when the next
    /// cell is the target itself and is not free (<see cref="FieldCollision.IsFree"/>).
    /// </summary>
    public Cell? NextStep(Cell from, Cell destination) =>
        ResolveStep(from, destination) is { BlockedAtTarget: false } step ? step.Next : null;

    /// <summary>✅ <c>0x00b43200</c> / <c>0x00b430f0</c>: raw A* search returning the first step without the final-cell free check.</summary>
    public bool TryFindFirstStep(Cell from, Cell destination, out Cell next)
    {
        next = from;
        if (from == destination)
            return false;

        int manhattan = Manhattan(from, destination);
        int maxG = Math.Min(manhattan * 2 + 1, MaxDepth);

        var open = new List<Node>();
        var byCell = new Dictionary<Cell, Node>();
        var start = new Node(from, g: 0, h: manhattan, parent: null);
        open.Add(start);
        byCell[from] = start;

        Node? goal = null;
        while (open.Count > 0)
        {
            var current = open[0];
            if (current.G >= maxG)
                return false;
            if (current.Cell == destination)
            {
                goal = current;
                break;
            }
            open.RemoveAt(0);
            current.Closed = true;

            foreach (var offset in Neighbors)
            {
                var neighbor = new Cell(current.Cell.X + offset.X, current.Cell.Y + offset.Y);
                if (!collision.CanPathThrough(neighbor.X, neighbor.Y, destination.X, destination.Y))
                    continue;
                ExpandNeighbor(current, neighbor, destination, open, byCell);
            }
        }

        if (goal is null)
            return false;

        // ✅ 0x00b43200: walk back until g <= 1 to obtain the first step from start
        var step = goal;
        while (step.G > 1 && step.Parent is not null)
            step = step.Parent;
        next = step.Cell;
        return true;
    }

    private static int Manhattan(Cell a, Cell b) => Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);

    private static void ExpandNeighbor(Node current, Cell neighbor, Cell destination, List<Node> open, Dictionary<Cell, Node> byCell)
    {
        int newG = current.G + 1;
        if (byCell.TryGetValue(neighbor, out var existing))
        {
            (current.Children ??= []).Add(existing);
            if (newG < existing.G)
            {
                existing.Parent = current;
                existing.G = newG;
                // ✅ 0x00b43520 lines 90-110: open-list nodes update G/F in place without reinserting; closed nodes propagate down
                if (existing.Closed)
                    PropagateDown(existing);
            }
            return;
        }

        var node = new Node(neighbor, newG, Manhattan(neighbor, destination), current);
        byCell[neighbor] = node;
        (current.Children ??= []).Add(node);
        InsertOpen(open, node);
    }

    /// <summary>✅ <c>0x00b43520</c>: inserts sorted by F ascending, placing ties before existing nodes (<c>&lt;=</c>).</summary>
    private static void InsertOpen(List<Node> open, Node node)
    {
        int f = node.F;
        int i = 0;
        while (i < open.Count && f > open[i].F)
            i++;
        open.Insert(i, node);
    }

    /// <summary>✅ <c>0x00b436e0</c>: propagates a shorter G cost through already-closed descendants.</summary>
    private static void PropagateDown(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var parent = stack.Pop();
            if (parent.Children is null)
                continue;
            int childG = parent.G + 1;
            foreach (var child in parent.Children)
            {
                if (childG < child.G)
                {
                    child.Parent = parent;
                    child.G = childG;
                    stack.Push(child);
                }
            }
        }
    }
}
