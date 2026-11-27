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
public sealed class FieldPathfinder(FieldCollision collision)
{
    /// <summary>✅ <c>0x00b430f0</c>: maximum path depth (<c>0x19 = 25</c>).</summary>
    public const int MaxDepth = 25;

    // ✅ 0x00b433d0: exact 8-neighbour expansion order (NW, N, NE, E, SE, S, SW, W)
    private static readonly (int Dx, int Dy)[] Neighbors =
    [
        (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0),
    ];

    private sealed class Node(int x, int y, int g, int h, Node? parent)
    {
        public int X { get; } = x;
        public int Y { get; } = y;
        public int G { get; set; } = g;
        public int H { get; } = h;
        public int F => G + H;
        public Node? Parent { get; set; } = parent;
        public bool Closed { get; set; }
        public List<Node>? Children { get; set; }
    }

    /// <summary>
    /// ✅ <c>0x00b43200</c> + <c>0x00b35b30</c> final-cell check: returns the next adjacent cell from
    /// (<paramref name="fromX"/>, <paramref name="fromY"/>) toward (<paramref name="destX"/>, <paramref name="destY"/>),
    /// or <c>null</c> when already there, unreachable within <see cref="MaxDepth"/>, or when the next cell is the target
    /// itself and is not free (<see cref="FieldCollision.IsFree"/>).
    /// </summary>
    public (int NextX, int NextY)? NextStep(int fromX, int fromY, int destX, int destY)
    {
        if (fromX == destX && fromY == destY)
            return null;
        if (!TryFindFirstStep(fromX, fromY, destX, destY, out int nextX, out int nextY))
            return null;
        if (nextX == destX && nextY == destY && !collision.IsFree(nextX, nextY))
            return null;
        return (nextX, nextY);
    }

    /// <summary>✅ <c>0x00b43200</c> / <c>0x00b430f0</c>: raw A* search returning the first step without the final-cell free check.</summary>
    public bool TryFindFirstStep(int fromX, int fromY, int destX, int destY, out int nextX, out int nextY)
    {
        nextX = -1;
        nextY = -1;
        if (fromX == destX && fromY == destY)
            return false;

        int manhattan = Math.Abs(destX - fromX) + Math.Abs(destY - fromY);
        int maxG = Math.Min(manhattan * 2 + 1, MaxDepth);

        var open = new List<Node>();
        var byCell = new Dictionary<(int, int), Node>();
        var start = new Node(fromX, fromY, g: 0, h: manhattan, parent: null);
        open.Add(start);
        byCell[(fromX, fromY)] = start;

        Node? goal = null;
        while (open.Count > 0)
        {
            var current = open[0];
            if (current.G >= maxG)
                return false;
            if (current.X == destX && current.Y == destY)
            {
                goal = current;
                break;
            }
            open.RemoveAt(0);
            current.Closed = true;

            foreach (var (dx, dy) in Neighbors)
            {
                int nx = current.X + dx;
                int ny = current.Y + dy;
                if (!collision.CanPathThrough(nx, ny, destX, destY))
                    continue;
                ExpandNeighbor(current, nx, ny, destX, destY, open, byCell);
            }
        }

        if (goal is null)
            return false;

        // ✅ 0x00b43200: walk back until g <= 1 to obtain the first step from start
        var step = goal;
        while (step.G > 1 && step.Parent is not null)
            step = step.Parent;
        nextX = step.X;
        nextY = step.Y;
        return true;
    }

    private static void ExpandNeighbor(Node current, int nx, int ny, int destX, int destY, List<Node> open, Dictionary<(int, int), Node> byCell)
    {
        int newG = current.G + 1;
        if (byCell.TryGetValue((nx, ny), out var existing))
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

        int h = Math.Abs(destX - nx) + Math.Abs(destY - ny);
        var node = new Node(nx, ny, newG, h, current);
        byCell[(nx, ny)] = node;
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
