﻿using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BossMod.Endwalker.Ultimate.DSW2
{
    class P2SanctityOfTheWard1 : BossModule.Component
    {
        class ChargeInfo
        {
            public Actor Source;
            public List<Vector3> Positions = new();
            public List<Vector3> Spheres = new();
            public bool Clockwise;

            public ChargeInfo(Actor source, bool clockwise)
            {
                Source = source;
                Clockwise = clockwise;
            }
        }

        public bool GazeDone { get; private set; }
        public int NumSeverCasts { get; private set; }
        private Vector3? _eyePosition;
        private float? _severStartDir;
        private Actor?[] _severTargets = { null, null };
        private ChargeInfo?[] _charges = { null, null };

        private static float _severRadius = 6;
        private static float _chargeHalfWidth = 3;
        private static float _brightflareRadius = 9;

        private static float _eyeOuterH = 10;
        private static float _eyeOuterV = 6;
        private static float _eyeInnerR = 4;
        private static float _eyeOuterR = (_eyeOuterH * _eyeOuterH + _eyeOuterV * _eyeOuterV) / (2 * _eyeOuterV);
        private static float _eyeOffsetV = _eyeOuterR - _eyeOuterV;
        private static float _eyeHalfAngle = MathF.Asin(_eyeOuterH / _eyeOuterR);

        public override void AddHints(BossModule module, int slot, Actor actor, BossModule.TextHints hints, BossModule.MovementHints? movementHints)
        {
            if (_eyePosition != null)
            {
                var actorForward = GeometryUtils.DirectionToVec3(actor.Rotation);
                if (FacingEye(actor.Position, actorForward, _eyePosition.Value) || FacingEye(actor.Position, actorForward, module.PrimaryActor.Position))
                    hints.Add("Turn away from gaze!");
            }

            if (NumSeverCasts < 4)
            {
                if (_severTargets.Contains(actor))
                {
                    // TODO: check 'far enough'?
                    if (module.Raid.WithoutSlot().InRadiusExcluding(actor, _severRadius).Count() < 3)
                        hints.Add("Stack in fours!");
                }
                else
                {
                    if (!_severTargets.Any(s => s == null || GeometryUtils.PointInCircle(s.Position - actor.Position, _severRadius)))
                        hints.Add("Stack in fours!");
                }
            }

            if (ImminentCharges().Any(fromTo => GeometryUtils.PointInRect(actor.Position - fromTo.Item1, fromTo.Item2 - fromTo.Item1, _chargeHalfWidth)))
                hints.Add("GTFO from charge!");
            if (ImminentSpheres().Any(s => GeometryUtils.PointInCircle(actor.Position - s, _brightflareRadius)))
                hints.Add("GTFO from sphere!");
        }

        public override void DrawArenaBackground(BossModule module, int pcSlot, Actor pc, MiniArena arena)
        {
            foreach (var (from, to) in ImminentCharges())
                arena.ZoneQuad(from, to, _chargeHalfWidth, arena.ColorAOE);
            foreach (var sphere in ImminentSpheres())
                arena.ZoneCircle(sphere, _brightflareRadius, arena.ColorAOE);
        }

        public override void DrawArenaForeground(BossModule module, int pcSlot, Actor pc, MiniArena arena)
        {
            if (_eyePosition != null)
            {
                DrawEye(arena, _eyePosition.Value);
                DrawEye(arena, module.PrimaryActor.Position);
            }

            if (NumSeverCasts < 4)
            {
                var source = module.Enemies(OID.SerZephirin).FirstOrDefault();
                arena.Actor(source, arena.ColorDanger);

                var target = _severTargets[NumSeverCasts % 2];
                if (source != null && target != null)
                    arena.AddLine(source.Position, target.Position, arena.ColorDanger);

                foreach (var p in module.Raid.WithoutSlot())
                {
                    if (_severTargets.Contains(p))
                    {
                        arena.Actor(p, arena.ColorPlayerInteresting);
                        arena.AddCircle(p.Position, _severRadius, arena.ColorDanger);
                    }
                    else
                    {
                        arena.Actor(p, arena.ColorPlayerGeneric);
                    }
                }
            }

            // TODO: select safe spot based on some configurable condition...
            if (_severStartDir != null && _charges[0] != null && _charges[1] != null && _charges[0]!.Clockwise == _charges[1]!.Clockwise)
            {
                var dir = _severStartDir.Value + (_charges[0]!.Clockwise ? -1 : 1) * MathF.PI / 8;
                DrawSafeSpot(arena, dir);
                DrawSafeSpot(arena, dir + MathF.PI);
            }
        }

        public override void OnEventCast(BossModule module, CastEvent info)
        {
            if (!info.IsSpell())
                return;
            switch ((AID)info.Action.ID)
            {
                case AID.DragonsGazeAOE:
                case AID.DragonsGlory:
                    _eyePosition = null;
                    GazeDone = true;
                    break;
                case AID.SacredSever:
                    ++NumSeverCasts;
                    break;
                case AID.ShiningBlade:
                    var charge = Array.Find(_charges, c => c?.Source.InstanceID == info.CasterID);
                    if (charge?.Positions.Count > 0)
                        charge.Positions.RemoveAt(0);
                    break;
                case AID.BrightFlare:
                    var sphere = module.WorldState.Actors.Find(info.CasterID);
                    if (sphere != null)
                        foreach (var c in _charges)
                            if (c != null)
                                c.Spheres.RemoveAll(s => Utils.AlmostEqual(s, sphere.Position, 3));
                    break;
            }
        }

        public override void OnEventIcon(BossModule module, ulong actorID, uint iconID)
        {
            switch ((IconID)iconID)
            {
                case IconID.SacredSever1:
                    _severTargets[0] = module.WorldState.Actors.Find(actorID);
                    InitChargesAndSafeSpots(module);
                    break;
                case IconID.SacredSever2:
                    _severTargets[1] = module.WorldState.Actors.Find(actorID);
                    InitChargesAndSafeSpots(module);
                    break;
            }
        }

        public override void OnEventEnvControl(BossModule module, uint featureID, byte index, uint state)
        {
            // seen indices: 2 = E, 5 = SW, 6 = W => inferring 0=N, 1=NE, ... cw order
            if (featureID == 0x8003759A && state == 0x00020001 && index <= 7)
            {
                _eyePosition = module.Arena.WorldCenter + 40 * GeometryUtils.DirectionToVec3(MathF.PI - index * MathF.PI / 4);
            }
        }

        private bool FacingEye(Vector3 actorPosition, Vector3 actorDirection, Vector3 eyePosition)
        {
            return Vector3.Dot(eyePosition - actorPosition, actorDirection) >= 0;
        }

        private void DrawEye(MiniArena arena, Vector3 position)
        {
            var dir = Vector3.Normalize(position - arena.WorldCenter);
            var eyeCenter = arena.ScreenCenter + arena.RotatedCoords(dir.XZ()) * (arena.ScreenHalfSize + arena.ScreenMarginSize / 2);
            var dl = ImGui.GetWindowDrawList();
            dl.PathArcTo(eyeCenter - new Vector2(0, _eyeOffsetV), _eyeOuterR,  MathF.PI / 2 + _eyeHalfAngle,  MathF.PI / 2 - _eyeHalfAngle);
            dl.PathArcTo(eyeCenter + new Vector2(0, _eyeOffsetV), _eyeOuterR, -MathF.PI / 2 + _eyeHalfAngle, -MathF.PI / 2 - _eyeHalfAngle);
            dl.PathFillConvex(arena.ColorEnemy);
            dl.AddCircleFilled(eyeCenter, _eyeInnerR, arena.ColorBorder);
        }

        private void DrawSafeSpot(MiniArena arena, float dir)
        {
            arena.AddCircle(arena.WorldCenter + 20 * GeometryUtils.DirectionToVec3(dir), 2, arena.ColorSafe);
        }

        private void InitChargesAndSafeSpots(BossModule module)
        {
            if (_severStartDir == null)
            {
                var source = module.Enemies(OID.SerZephirin).FirstOrDefault();
                if (source != null)
                    _severStartDir = GeometryUtils.DirectionFromVec3(source.Position - module.Arena.WorldCenter);
            }

            if (_charges[0] == null)
                _charges[0] = BuildChargeInfo(module, OID.SerAdelphel);
            if (_charges[1] == null)
                _charges[1] = BuildChargeInfo(module, OID.SerJanlenoux);
        }

        private ChargeInfo? BuildChargeInfo(BossModule module, OID oid)
        {
            var actor = module.Enemies(oid).FirstOrDefault();
            if (actor == null)
                return null;

            // so far I've only seen both enemies starting at (+-5, 0)
            if (!Utils.AlmostEqual(actor.Position.Z, module.Arena.WorldCenter.Z, 1))
                return null;
            if (!Utils.AlmostEqual(MathF.Abs(actor.Position.X - module.Arena.WorldCenter.X), 5, 1))
                return null;

            bool right = actor.Position.X > module.Arena.WorldCenter.X;
            bool facingSouth = Utils.AlmostEqual(actor.Rotation, 0, 0.1f);
            var res = new ChargeInfo(actor, right == facingSouth);
            float firstPointDir = actor.Rotation;
            float angleBetweenPoints = (res.Clockwise ? -1 : 1) * 5 * MathF.PI / 8;

            res.Positions.Add(actor.Position);
            Action<float> addPosition = dir => res.Positions.Add(module.Arena.WorldCenter + 21 * GeometryUtils.DirectionToVec3(dir));
            addPosition(firstPointDir);
            addPosition(firstPointDir + angleBetweenPoints);
            addPosition(firstPointDir + angleBetweenPoints * 2);

            res.Spheres.Add(res.Positions[0]);
            res.Spheres.Add((res.Positions[0] + res.Positions[1]) / 2);
            res.Spheres.Add(res.Positions[1]);
            res.Spheres.Add((res.Positions[1] * 2 + res.Positions[2]) / 3);
            res.Spheres.Add((res.Positions[1] + res.Positions[2] * 2) / 3);
            res.Spheres.Add(res.Positions[2]);
            res.Spheres.Add((res.Positions[2] * 2 + res.Positions[3]) / 3);
            res.Spheres.Add((res.Positions[2] + res.Positions[3] * 2) / 3);
            res.Spheres.Add(res.Positions[3]);
            return res;
        }

        private IEnumerable<(Vector3, Vector3)> ImminentCharges()
        {
            foreach (var c in _charges)
            {
                if (c == null)
                    continue;
                for (int i = 1; i < c.Positions.Count; ++i)
                    yield return (c.Positions[i - 1], c.Positions[i]);
            }
        }

        private IEnumerable<Vector3> ImminentSpheres()
        {
            foreach (var c in _charges)
            {
                if (c == null)
                    continue;
                foreach (var s in c.Spheres.Take(6))
                    yield return s;
            }
        }
    }
}