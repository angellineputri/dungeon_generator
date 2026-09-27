using System.Collections.Generic;
using UnityEngine;

public static class Pathfinding
{
    private class Node
    {
        public Vector2Int pos;
        public Node parent;
        public int gCost;
        public int hCost;
        public int fCost => gCost + hCost;
    }

    public static List<Vector2Int> FindPath(DungeonGenerator dungeon, Vector2Int start, Vector2Int target)
    {
        var openList = new List<Node>();
        var closedSet = new HashSet<Vector2Int>();

        Node startNode = new Node { pos = start, gCost = 0, hCost = Heuristic(start, target) };
        openList.Add(startNode);

        Dictionary<Vector2Int, Node> allNodes = new Dictionary<Vector2Int, Node> { { start, startNode } };

        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (openList.Count > 0)
        {
            Node current = openList[0];
            for (int i = 1; i < openList.Count; i++)
                if (openList[i].fCost < current.fCost ||
                   (openList[i].fCost == current.fCost && openList[i].hCost < current.hCost))
                    current = openList[i];

            if (current.pos == target)
                return ReconstructPath(current);

            openList.Remove(current);
            closedSet.Add(current.pos);

            foreach (var dir in directions)
            {
                Vector2Int neighborPos = current.pos + dir;

                if (!dungeon.IsWalkable(neighborPos.x, neighborPos.y)) continue;
                if (closedSet.Contains(neighborPos)) continue;

                int tentativeG = current.gCost + 1;

                if (!allNodes.TryGetValue(neighborPos, out Node neighborNode))
                {
                    neighborNode = new Node { pos = neighborPos, hCost = Heuristic(neighborPos, target) };
                    allNodes[neighborPos] = neighborNode;
                }

                if (tentativeG < neighborNode.gCost || !openList.Contains(neighborNode))
                {
                    neighborNode.gCost = tentativeG;
                    neighborNode.parent = current;

                    if (!openList.Contains(neighborNode))
                        openList.Add(neighborNode);
                }
            }
        }

        return null;
    }

    private static int Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static List<Vector2Int> ReconstructPath(Node endNode)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Node current = endNode;
        while (current != null)
        {
            path.Add(current.pos);
            current = current.parent;
        }
        path.Reverse();
        return path;
    }
}
