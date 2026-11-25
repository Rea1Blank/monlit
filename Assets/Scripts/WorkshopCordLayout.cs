using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monlit
{
    internal static class WorkshopCordLayout
    {
        // Allow room to carry and rotate a piece near a completed layout.
        private const float ApproachRadius = .85f;
        private const float ApproachAngle = 50f;
        private const float HandlingSlack = .14f;
        // More than both seats' combined position/yaw tolerance and joint play.
        private const float WrongSeatClearance = .45f;

        internal struct Eye
        {
            public int Group;
            public Vector3 Start;
            public Vector3 SeatCenter;
            public Vector3[] Docked;
            // Every placement of this piece inside its own silhouette on the cut grid.
            public Vector3[] Fitting;
        }

        internal struct Link
        {
            public int A, B;
            public float Length;
        }

        internal static Link[] Plan(Eye[] eyes)
        {
            var plan = new Connections(eyes);
            return plan.Finish();
        }

        // The twins have identical local geometry and eyelets. At the other twin's
        // seat their possible world eye positions are exactly the other Docked array.
        internal static bool TryPlanSenior(Eye[] eyes, int twinA, int twinB, out Link[] links)
        {
            var first = FindTwinLinks(eyes, twinA, twinB);
            var second = FindTwinLinks(eyes, twinB, twinA);
            var plan = new Connections(eyes);
            int bestConnections = -1;
            float bestCost = float.PositiveInfinity;
            Link chosenA = default, chosenB = default;
            foreach (var a in first)
            foreach (var b in second)
            {
                if (a.link.B == b.link.B) continue;
                plan.Reset();
                plan.Add(a.link);
                float cost = a.cost + b.cost + plan.CrossingCost(b.link.A, b.link.B);
                plan.Add(b.link);
                int reachable = plan.ReachableConnections();
                if (reachable < bestConnections || (reachable == bestConnections && cost >= bestCost)) continue;
                bestConnections = reachable;
                bestCost = cost;
                chosenA = a.link;
                chosenB = b.link;
            }

            if (bestConnections < 0)
            {
                links = Array.Empty<Link>();
                return false;
            }
            plan.Reset();
            plan.Add(chosenA);
            plan.Add(chosenB);
            links = plan.Finish();
            return true;
        }

        private static List<(Link link, float cost)> FindTwinLinks(Eye[] eyes, int twin, int other)
        {
            var choices = new List<(Link, float)>();
            // Every support is checked against the same wrong silhouette. Limit each
            // radius query to its strip of cut-grid positions.
            var wrong = (Vector3[])(eyes[other].Fitting ?? eyes[other].Docked).Clone();
            Array.Sort(wrong, (a, b) => a.x.CompareTo(b.x));
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            foreach (var p in wrong)
            {
                minZ = Mathf.Min(minZ, p.z);
                maxZ = Mathf.Max(maxZ, p.z);
            }
            for (int support = 0; support < eyes.Length; support++)
            {
                if (support == twin || support == other) continue;
                float cost = ScorePair(eyes[twin], eyes[support], out float length);
                if (ExcludesSwap(wrong, minZ, maxZ, eyes[support], length))
                    choices.Add((new Link { A = twin, B = support, Length = length }, cost));
            }
            return choices;
        }

        private static bool ExcludesSwap(Vector3[] wrong, float minZ, float maxZ, Eye support, float length)
        {
            // Even if the player repacks the coloured silhouettes into another
            // exact tiling, a twin cannot reach the group matching its paint.
            float minimum = length + WrongSeatClearance;
            float minimumSquared = minimum * minimum;
            foreach (var eye in support.Fitting ?? support.Docked)
            {
                if (eye.z + minimum < minZ || eye.z - minimum > maxZ) continue;
                int lo = 0, hi = wrong.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (wrong[mid].x < eye.x - minimum) lo = mid + 1;
                    else hi = mid;
                }
                for (int i = lo; i < wrong.Length && wrong[i].x <= eye.x + minimum; i++)
                    if ((wrong[i] - eye).sqrMagnitude < minimumSquared) return false;
            }
            return true;
        }

        // Each body can carry one joint. Track both connected groups and the number
        // of free eyes they still have, including when four eyes are reserved for twins.
        private sealed class Connections
        {
            private readonly Eye[] eyes;
            private readonly List<Link> links = new List<Link>();
            private readonly bool[] used, tiedInside;
            private readonly int[] component, free, population;

            public Connections(Eye[] eyes)
            {
                this.eyes = eyes;
                int groups = 0;
                foreach (var eye in eyes) groups = Math.Max(groups, eye.Group + 1);
                used = new bool[eyes.Length];
                tiedInside = new bool[groups];
                component = new int[groups];
                free = new int[groups];
                population = new int[groups];
                foreach (var eye in eyes) population[eye.Group]++;
                Reset();
            }

            public void Reset()
            {
                foreach (var link in links) used[link.A] = used[link.B] = false;
                links.Clear();
                Array.Clear(tiedInside, 0, tiedInside.Length);
                Array.Copy(population, free, free.Length);
                for (int g = 0; g < component.Length; g++) component[g] = g;
            }

            public void Add(Link link)
            {
                links.Add(link);
                used[link.A] = used[link.B] = true;
                int groupA = eyes[link.A].Group, groupB = eyes[link.B].Group;
                if (groupA == groupB) tiedInside[groupA] = true;
                int a = component[groupA], b = component[groupB];
                free[a]--;
                free[b]--;
                if (a == b) return;
                free[a] += free[b];
                free[b] = 0;
                for (int g = 0; g < component.Length; g++)
                    if (component[g] == b) component[g] = a;
            }

            public int ReachableConnections()
            {
                int separate = 0, open = 0, eyesLeft = 0;
                for (int g = 0; g < component.Length; g++)
                {
                    if (component[g] != g) continue;
                    separate++;
                    if (free[g] > 0) open++;
                    eyesLeft += free[g];
                }
                int extra = Math.Max(0, Math.Min(open - 1, eyesLeft / 2));
                return component.Length - separate + extra;
            }

            public Link[] Finish()
            {
                // The physical length and base score never change when we add links.
                // Sort them once. Crossing penalties are nonnegative, so a pass may
                // stop as soon as no remaining candidate can improve the best fit.
                int available = eyes.Length - 2 * links.Count;
                var candidates = new List<(Link link, float cost)>(available * (available - 1) / 2);
                for (int a = 0; a < eyes.Length - 1; a++)
                for (int b = a + 1; b < eyes.Length; b++)
                {
                    if (used[a] || used[b]) continue;
                    float cost = ScorePair(eyes[a], eyes[b], out float length);
                    candidates.Add((new Link { A = a, B = b, Length = length }, cost));
                }
                candidates.Sort((a, b) => a.cost.CompareTo(b.cost));
                // First bridge as many groups as the remaining independent eyes allow.
                while (TryAddBest(candidates, true)) { }
                while (links.Count < 2 * (component.Length - 1) && TryAddBest(candidates, false)) { }
                return links.ToArray();
            }

            private bool TryAddBest(List<(Link link, float cost)> candidates, bool bridge)
            {
                float best = float.PositiveInfinity;
                Link chosen = default;
                bool hasSpareEyes = Array.Exists(free, slots => slots > 1);
                foreach (var candidate in candidates)
                {
                    if (candidate.cost > best) break;
                    int a = candidate.link.A, b = candidate.link.B;
                    if (used[a] || used[b]) continue;
                    int groupA = eyes[a].Group, groupB = eyes[b].Group;
                    if (bridge)
                    {
                        int ca = component[groupA], cb = component[groupB];
                        if (ca == cb) continue;
                        // Joining two last eyes would strand both components whenever
                        // another component could serve as the middle of the chain.
                        if (free[ca] == 1 && free[cb] == 1 && hasSpareEyes) continue;
                    }
                    else if (groupA != groupB || tiedInside[groupA]) continue;
                    float cost = candidate.cost + CrossingCost(a, b);
                    if (cost > best || (cost == best && (a > chosen.A || (a == chosen.A && b >= chosen.B)))) continue;
                    best = cost;
                    chosen = candidate.link;
                }
                if (float.IsPositiveInfinity(best)) return false;
                Add(chosen);
                return true;
            }

            public float CrossingCost(int a, int b)
            {
                float cost = 0f;
                foreach (var link in links)
                {
                    var c = eyes[link.A];
                    var d = eyes[link.B];
                    if (Crosses(eyes[a].Start, eyes[b].Start, c.Start, d.Start)) cost += .7f;
                    if (Crosses(eyes[a].Docked[0], eyes[b].Docked[0], c.Docked[0], d.Docked[0])) cost += 1.4f;
                }
                return cost;
            }
        }

        private static float ScorePair(Eye first, Eye second, out float length)
        {
            float shortest = Vector3.Distance(first.Start, second.Start);
            float lift = Mathf.Max(WorkshopRobot.GripHeight, WorkshopPiece.CarryCaptureHeight);
            var startDelta = first.Start - second.Start;
            startDelta.y = Mathf.Abs(startDelta.y) + lift;
            length = startDelta.magnitude;
            float mixedSpan = 0f;

            foreach (var a in first.Docked)
            {
                mixedSpan = Mathf.Max(mixedSpan, Vector3.Distance(a, second.Start));
                foreach (var b in second.Docked)
                {
                    shortest = Mathf.Min(shortest, Vector3.Distance(a, b));
                    length = Mathf.Max(length, ApproachSpan(first.SeatCenter, a, b));
                    length = Mathf.Max(length, ApproachSpan(second.SeatCenter, b, a));
                }
            }
            foreach (var b in second.Docked)
                mixedSpan = Mathf.Max(mixedSpan, Vector3.Distance(first.Start, b));

            length += HandlingSlack;
            // Prefer little loose cord at rest. Between similar pairs, prefer the one
            // that needs less towing when either end leaves the monolith first.
            return 14f * (length - shortest) + length + 2f * Mathf.Max(0f, mixedSpan - length);
        }

        private static float ApproachSpan(Vector3 seatCenter, Vector3 dockedEye, Vector3 fixedEye)
        {
            var outward = new Vector2(seatCenter.x - fixedEye.x, seatCenter.z - fixedEye.z);
            var eyeOffset = new Vector2(dockedEye.x - seatCenter.x, dockedEye.z - seatCenter.z);
            // The eye is farthest away when rotated toward the outward direction,
            // within the handling allowance around the landing position.
            float angle = Mathf.Max(0f, Vector2.Angle(outward, eyeOffset) - ApproachAngle);
            float squared = outward.sqrMagnitude + eyeOffset.sqrMagnitude +
                2f * outward.magnitude * eyeOffset.magnitude * Mathf.Cos(angle * Mathf.Deg2Rad);
            float horizontal = Mathf.Sqrt(squared) + ApproachRadius;
            float vertical = Mathf.Abs(dockedEye.y - fixedEye.y) + WorkshopPiece.CarryCaptureHeight;
            return Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
        }

        private static bool Crosses(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            return Side(a, b, c) * Side(a, b, d) < 0f && Side(c, d, a) * Side(c, d, b) < 0f;
        }

        private static float Side(Vector3 a, Vector3 b, Vector3 p)
        {
            return (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
        }
    }
}
