﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DataStructures.PriorityQueue;

public static class PathFinding
{
    // Simple Funnel Offset
    static float offsetMultiplier = 0.25f;

    // Return the shortest path as a sequence of points
    public static List<Vector2> GetShortestPath(List<MeshPolygon> navMesh, Vector2 startPoint, Vector2 destinationPoint)
    {
        // Get shortest path in polygons
        List<MeshPolygon> polygonPath =
            GetShortestPathPolygons(navMesh, startPoint, destinationPoint);

        List<Vector2> path = GetPathBySSFA(startPoint, destinationPoint, polygonPath);

        return path;
    }

    public static float GetShortestPathDistance(List<MeshPolygon> navMesh, Vector2 startPoint, Vector2 destinationPoint)
    {
        List<Vector2> path = GetShortestPath(navMesh, startPoint, destinationPoint);

        float totalDistance = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            totalDistance += Vector2.Distance(path[i], path[i + 1]);
        }

        return totalDistance;
    }


    // Receive Polygon start and end position and return Polygon based path
    private static List<MeshPolygon> GetShortestPathPolygons(List<MeshPolygon> navMesh, Vector2 start,
        Vector2 destination)
    {
        MeshPolygon startPolygon = GetCorrespondingPolygon(navMesh, start);
        MeshPolygon destinationPolygon = GetCorrespondingPolygon(navMesh, destination);

        if (startPolygon == null)
            return null;

        PriorityQueue<MeshPolygon, float> openList = new PriorityQueue<MeshPolygon, float>(0f);
        List<MeshPolygon> closedList = new List<MeshPolygon>();

        foreach (MeshPolygon p in navMesh)
        {
            p.SetgDistance(Mathf.Infinity);
            p.SethDistance(Mathf.Infinity);
            p.SetPreviousPolygon(null);
        }

        // Set Cost of starting Polygon
        startPolygon.SetgDistance(0f);
        // startPolygon.SethDistance(GetHeuristicValue(startPolygon, destinationPolygon));
        startPolygon.SethDistance(Vector2.Distance(start, destination));
        startPolygon.SetEntryPoint(start);


        // Add the starting polygon to the open list
        openList.Insert(startPolygon, startPolygon.GetgDistance() + startPolygon.GethDistance());

        while (!openList.IsEmpty())
        {
            MeshPolygon current = openList.Pop();

            foreach (KeyValuePair<int, MeshPolygon> pPair in current.GetAdjcentPolygons())
            {
                MeshPolygon p = pPair.Value;

                Vector2 possibleNextWaypoint = current.GetMidPointOfDiagonalNeighbor(p);

                if (!closedList.Contains(p))
                {
                    float gDistance = GetCostValue(current, p, destinationPolygon, destination);
                    // float hDistance = GetHeuristicValue(p, destinationPolygon);

                    float hDistance = Vector2.Distance(possibleNextWaypoint, destination);

                    if (p.GetgDistance() + p.GethDistance() > gDistance + hDistance)
                    {
                        p.SethDistance(hDistance);
                        p.SetgDistance(gDistance);

                        p.SetPreviousPolygon(current);
                        p.SetEntryPoint(possibleNextWaypoint);
                    }

                    openList.Insert(p, p.GetgDistance() + p.GethDistance());
                }
            }

            closedList.Add(current);

            // Stop the search if we reached the destination polygon
            if (current.Equals(destinationPolygon))
                break;
        }

        // Get the path starting from the goal node to the last polygon before the starting polygon
        List<MeshPolygon> path = new List<MeshPolygon>();

        MeshPolygon currentPolygon = destinationPolygon;
        while (currentPolygon.GetPreviousPolygon() != null)
        {
            path.Add(currentPolygon);

            if (currentPolygon.GetPreviousPolygon() == null)
                break;

            currentPolygon = currentPolygon.GetPreviousPolygon();
        }

        // Add the first polygon to the path
        path.Add(startPolygon);

        // reverse the path so it start from the start node
        path.Reverse();


        return path;
    }


    // Get the Cost Value (G) for the Mesh polygons
    static float GetCostValue(MeshPolygon previousPolygon, MeshPolygon currentPolygon, MeshPolygon destinationPolygon,
        Vector2 destination)
    {
        float costValue = previousPolygon.GetgDistance();

        Vector2? previousWaypoint = previousPolygon.GetEntryPoint();

        Vector2 possibleNextWaypoint;

        if (currentPolygon.Equals(destinationPolygon))
            possibleNextWaypoint = destination;
        else
            possibleNextWaypoint = currentPolygon.GetMidPointOfDiagonalNeighbor(previousPolygon);

        // Euclidean Distance
        float
            // distance = Vector2.Distance(previousPolygon.GetCentroidPosition(), currentPolygon.GetCentroidPosition());
            distance = Vector2.Distance(previousWaypoint.Value, possibleNextWaypoint);

        costValue += distance;

        return costValue;
    }


    // Get the Cost Value (G) for the Waypoints roadmap
    static float GetCostValue(WayPoint previousWayPoint, WayPoint currentWayPoint)
    {
        float costValue = previousWayPoint.gDistance;

        // Euclidean Distance
        float distance = Vector2.Distance(previousWayPoint.GetPosition(), currentWayPoint.GetPosition());

        costValue += distance;

        return costValue;
    }


    // Get heuristic value for mesh polygons
    static float GetHeuristicValue(MeshPolygon currentPolygon, MeshPolygon goal)
    {
        float heuristicValue = Vector2.Distance(currentPolygon.GetCentroidPosition(), goal.GetCentroidPosition());

        return heuristicValue;
    }


    // Get heuristic value for way points road map
    static float GetHeuristicValue(WayPoint currentWayPoint, WayPoint goal)
    {
        float heuristicValue = Vector2.Distance(currentWayPoint.GetPosition(), goal.GetPosition());

        return heuristicValue;
    }

    // Get the polygon that contains the specified point
    private static MeshPolygon GetCorrespondingPolygon(List<MeshPolygon> navMesh, Vector2 point)
    {
        // Check all polygons in the navmesh if they contain the point 
        foreach (var p in navMesh)
            if (p.IsPointInPolygon(point, true))
                return p;

        // In case the point is out of all polygons 
        // Loop through the polygons and intersect them with the point to their centroids and return the closest intersection point

        float minDistance = Mathf.Infinity;
        MeshPolygon closestPolygon = null;

        foreach (var p in navMesh)
        {
            Vector2 intersection = p.GetClosestIntersectionPoint(point);
            float distance = Vector2.Distance(intersection, point);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestPolygon = p;
            }
        }


        return closestPolygon;
    }

    public static Polygon GetCorrespondingPolygon(List<VisibilityPolygon> navMesh, Vector2 point)
    {
        foreach (var p in navMesh)
            if (p.IsPointInPolygon(point, true))
                return p;

        return null;
    }


    // Get the path using the simple stupid funnel algorithm
    // http://ahamnett.blogspot.com/2012/10/funnel-algorithm.html
    static List<Vector2> GetPathBySSFA(Vector2 startPoint, Vector2 destinationPoint, List<MeshPolygon> polygonPath)
    {
        List<Vector2> path = new List<Vector2>();
        Vector2[] leftVertices = new Vector2[polygonPath.Count + 1];
        Vector2[] rightVertices = new Vector2[polygonPath.Count + 1];

        // Initialise portal vertices first point.
        leftVertices[0] = startPoint;
        rightVertices[0] = startPoint;

        // Add the gates vertices
        for (int i = 0; i < polygonPath.Count - 1; i++)
            polygonPath[i]
                .GetDiagonalOfNeighbor(polygonPath[i + 1], out leftVertices[i + 1], out rightVertices[i + 1]);

        // Initialise portal vertices last point.
        leftVertices[polygonPath.Count] = destinationPoint;
        rightVertices[polygonPath.Count] = destinationPoint;

        int left = 1;
        int right = 1;
        Vector2 apex = startPoint;
        // Step through channel.

        // Go through the polygons
        for (int i = 1; i <= polygonPath.Count; i++)
        {
            // If new left vertex is different from the current, the check
            if (leftVertices[i] != leftVertices[left] && i > left)
            {
                // If new side does not widen funnel, update.
                if (GeometryHelper.SignedAngle(apex, leftVertices[left], leftVertices[i]) <= 0f)
                {
                    // If new side crosses other side, update apex.
                    if (GeometryHelper.SignedAngle(apex, rightVertices[right], leftVertices[i]) <= 0f)
                    {
                        int next = right;
                        int prev = right;

                        // Find next vertex.
                        for (int j = next; j <= polygonPath.Count; j++)
                        {
                            if (rightVertices[j] != rightVertices[next])
                            {
                                next = j;
                                break;
                            }
                        }

                        // Find previous vertex.
                        for (int j = prev; j >= 0; j--)
                        {
                            if (rightVertices[j] != rightVertices[prev])
                            {
                                prev = j;
                                break;
                            }
                        }

                        // Calculate line angles.
                        float nextAngle = Mathf.Atan2(rightVertices[next].y - rightVertices[right].y,
                            rightVertices[next].x - rightVertices[right].x);
                        float prevAngle = Mathf.Atan2(rightVertices[right].y - rightVertices[prev].y,
                            rightVertices[right].x - rightVertices[prev].x);

                        // Calculate minimum distance between line angles.
                        float distance = nextAngle - prevAngle;

                        if (Mathf.Abs(distance) > Mathf.PI)
                        {
                            distance -= 2f * (distance > 0 ? Mathf.PI : -Mathf.PI);
                        }

                        // Calculate left perpendicular to average angle.
                        float angle = prevAngle + (distance / 2) + Mathf.PI * 0.5f;

                        Vector2 normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                        float offsetDistance = offsetMultiplier;

                        Vector2 newApex = rightVertices[right];

                        // Add new offset apex to list and update right side.
                        if (left != polygonPath.Count && right != polygonPath.Count)
                        {
                            path.Add(newApex + normal * offsetDistance);
                        }

                        // Reset the funnel 
                        apex = newApex; // + normal * offsetDistance;
                        right = next;
                        left = next;
                        i = next;
                    }
                    else
                    {
                        // Narrow the funnel from left
                        left = i;
                    }
                }
            }

            // If new right vertex is different, process.
            if (rightVertices[i] != rightVertices[right] && i > right)
            {
                // If new side does not widen funnel, update.
                if (GeometryHelper.SignedAngle(apex, rightVertices[right], rightVertices[i]) >= 0f)
                {
                    // If new side crosses other side, update apex.
                    if (GeometryHelper.SignedAngle(apex, leftVertices[left], rightVertices[i]) >= 0f)
                    {
                        int next = left;
                        int prev = left;

                        // Find next vertex.
                        for (int j = next; j <= polygonPath.Count; j++)
                        {
                            if (leftVertices[j] != leftVertices[next])
                            {
                                next = j;
                                break;
                            }
                        }

                        // Find previous vertex.
                        for (int j = prev; j >= 0; j--)
                        {
                            if (leftVertices[j] != leftVertices[prev])
                            {
                                prev = j;
                                break;
                            }
                        }

                        // Calculate line angles.
                        float nextAngle = Mathf.Atan2(leftVertices[next].y - leftVertices[left].y,
                            leftVertices[next].x - leftVertices[left].x);
                        float prevAngle = Mathf.Atan2(leftVertices[left].y - leftVertices[prev].y,
                            leftVertices[left].x - leftVertices[prev].x);

                        // Calculate minimum distance between line angles.
                        float distance = nextAngle - prevAngle;

                        if (Mathf.Abs(distance) > Mathf.PI)
                        {
                            distance -= 2f * (distance > 0 ? Mathf.PI : -Mathf.PI);
                        }

                        // Calculate left perpendicular to average angle.
                        float angle = prevAngle + (distance / 2) + Mathf.PI * 0.5f;

                        Vector2 normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                        float offsetDistance = offsetMultiplier;

                        Vector2 newApex = leftVertices[left];

                        // Add new offset apex to list and update right side.
                        if (left != polygonPath.Count && right != polygonPath.Count)
                        {
                            path.Add(newApex - normal * offsetDistance);
                        }

                        // Reset the funnel
                        apex = newApex; // - normal * offsetDistance;
                        left = next;
                        right = next;
                        i = next;
                    }
                    else
                    {
                        // Narrow the funnel from right
                        right = i;
                    }
                }
            }
        }


        path.Add(destinationPoint);

        return path;
    }


    // Get shortest path on the road map
    // The start node is a node on the road map and the goal is the position of the phantom 
    // for ease of implementation we start the search from the goal to the start node
    public static List<Vector2> GetShortestPath(List<WayPoint> roadmap, InterceptionPoint goalPh, WayPoint start)
    {
        WayPoint goal = goalPh.destination;

        PriorityQueue<WayPoint, float> openList = new PriorityQueue<WayPoint, float>(0f);
        List<WayPoint> closedList = new List<WayPoint>();

        foreach (WayPoint p in roadmap)
        {
            p.gDistance = Mathf.Infinity;
            p.hDistance = Mathf.Infinity;
            p.parent = null;
        }

        // Set Cost of starting node
        start.gDistance = 0f;
        start.hDistance = Vector2.Distance(start.GetPosition(), goal.GetPosition());

        while (!openList.IsEmpty())
        {
            WayPoint current = openList.Pop();

            foreach (WayPoint p in current.GetConnections())
            {
                if (!closedList.Contains(p))
                {
                    float gDistance = GetCostValue(current, p);
                    float hDistance = GetHeuristicValue(current, goal);

                    if (p.gDistance + p.hDistance > gDistance + hDistance)
                    {
                        p.hDistance = hDistance;
                        p.gDistance = gDistance;

                        p.parent = current;
                    }

                    openList.Insert(p, p.gDistance + p.hDistance);
                }
            }

            closedList.Add(current);

            // Stop the search if we reached the destination way point
            if (current.Equals(goal))
                break;
        }

        List<Vector2> result = new List<Vector2>();

        WayPoint currentWayPoint = goal;
        while (currentWayPoint.parent != null)
        {
            result.Add(currentWayPoint.GetPosition());

            if (currentWayPoint.parent == null)
                break;

            currentWayPoint = currentWayPoint.parent;
        }

        // Add the first waypoint to the path
        result.Add(start.GetPosition());

        // and add the actual phantom node position since we didn't include it in the A* search
        result.Add(goalPh.position);

        // reverse the path so it start from the start node
        result.Reverse();


        return result;
    }


    public static float GetShortestPathDistance(List<WayPoint> roadmap, InterceptionPoint goalPh, WayPoint start)
    {
        List<Vector2> path = GetShortestPath(roadmap, goalPh, start);

        float totalDistance = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            totalDistance += Vector2.Distance(path[i], path[i + 1]);
        }

        return totalDistance;
    }

    // Helper function
    public static T KeyByValue<T, W>(this Dictionary<T, W> dict, W val)
    {
        T key = default;
        foreach (KeyValuePair<T, W> pair in dict)
        {
            if (EqualityComparer<W>.Default.Equals(pair.Value, val))
            {
                key = pair.Key;
                break;
            }
        }

        return key;
    }
}