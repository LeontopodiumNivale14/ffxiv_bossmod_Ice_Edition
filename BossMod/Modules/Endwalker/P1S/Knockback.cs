﻿using System.Linq;
using System.Numerics;

namespace BossMod.P1S
{
    using static BossModule;

    // state related to knockback + aoe mechanic
    // TODO: i'm not quite happy with implementation, consider revising...
    class Knockback : Component
    {
        public bool AOEDone { get; private set; } = false;
        private P1S _module;
        private bool _isFlare = false; // true -> purge aka flare (stay away from MT), false -> grace aka holy (stack to MT)
        private Actor? _knockbackTarget = null;
        private Vector3 _knockbackPos = new();

        private static float _kbDistance = 15;
        private static float _flareRange = 20; // this is tricky - range is actually 50, but it has some sort of falloff - not sure what is 'far enough'
        private static float _holyRange = 6;
        private static uint _colorAOETarget = 0xff8080ff;

        public Knockback(P1S module, uint knockbackTargetID, bool isFlare)
        {
            _module = module;
            _isFlare = isFlare;
            _knockbackTarget = _module.WorldState.Actors.Find(knockbackTargetID);
            if (_knockbackTarget == null)
                Service.Log("[P1S] Failed to determine knockback target");
        }

        public override void Update()
        {
            if (_knockbackTarget != null)
            {
                _knockbackPos = _knockbackTarget.Position;
                var boss = _module.Boss();
                if (boss?.CastInfo != null)
                {
                    _knockbackPos = AdjustPositionForKnockback(_knockbackPos, boss, _kbDistance);
                }
            }
        }

        public override void AddHints(int slot, Actor actor, TextHints hints, MovementHints? movementHints)
        {
            var boss = _module.Boss();
            if (boss == null)
                return;

            if (boss.CastInfo != null && actor == _knockbackTarget && !_module.Arena.InBounds(_knockbackPos))
            {
                hints.Add("About to be knocked into wall!");
            }

            float aoeRange = _isFlare ? _flareRange : _holyRange;
            if (boss.TargetID == actor.InstanceID)
            {
                // i'm the current tank - i should gtfo from raid if i'll get the flare -or- if i'm vulnerable (assuming i'll pop invul not to die)
                if (RaidShouldStack(actor))
                {
                    // check that raid is stacked on actor, except for vulnerable target (note that raid never stacks if knockback target is actor, since he is current aoe target)
                    if (_knockbackTarget != null && GeometryUtils.PointInCircle(actor.Position - _knockbackPos, aoeRange))
                    {
                        hints.Add("GTFO from co-tank!");
                    }
                    if (_module.Raid.WithoutSlot().InRadiusExcluding(actor, aoeRange).Count() < 7)
                    {
                        hints.Add("Stack with raid!");
                    }
                }
                else
                {
                    // check that raid is spread from actor
                    if (actor == _knockbackTarget)
                    {
                        hints.Add("Press invul!");
                    }
                    if (_module.Raid.WithoutSlot().InRadiusExcluding(actor, aoeRange).Any())
                    {
                        hints.Add("GTFO from raid!");
                    }
                }
            }
            else
            {
                // i'm not the current tank - I should gtfo if tank is invul soaking, from flare or from holy if i'm vulnerable, otherwise stack to current tank
                var target = _module.WorldState.Actors.Find(boss.TargetID);
                if (target == null)
                    return;

                if (RaidShouldStack(target) && actor != _knockbackTarget)
                {
                    // check that actor is stacked with tank
                    if (!GeometryUtils.PointInCircle(actor.Position - target.Position, aoeRange))
                    {
                        hints.Add("Stack with target!");
                    }
                }
                else
                {
                    // check that actor stays away from tank
                    var pos = actor == _knockbackTarget ? _knockbackPos : actor.Position;
                    if (GeometryUtils.PointInCircle(pos - target.Position, aoeRange))
                    {
                        hints.Add("GTFO from target!");
                    }
                }
            }
        }

        public override void DrawArenaForeground(int pcSlot, Actor pc, MiniArena arena)
        {
            var boss = _module.Boss();
            if (boss == null)
                return;

            if (boss.CastInfo != null && pc == _knockbackTarget && pc.Position != _knockbackPos)
            {
                arena.AddLine(pc.Position, _knockbackPos, arena.ColorDanger);
                arena.Actor(_knockbackPos, pc.Rotation, arena.ColorDanger);
            }

            var target = _module.WorldState.Actors.Find(boss.TargetID);
            if (target == null)
                return;

            var targetPos = target == _knockbackTarget ? _knockbackPos : target.Position;
            float aoeRange = _isFlare ? _flareRange : _holyRange;
            if (target == pc)
            {
                // there will be AOE around me, draw all players to help with positioning - note that we use position adjusted for knockback
                foreach (var player in _module.Raid.WithoutSlot())
                    arena.Actor(player, GeometryUtils.PointInCircle(player.Position - targetPos, aoeRange) ? arena.ColorPlayerInteresting : arena.ColorPlayerGeneric);
            }
            else
            {
                // draw AOE source
                arena.Actor(targetPos, target.Rotation, _colorAOETarget);
            }
            arena.AddCircle(targetPos, aoeRange, arena.ColorDanger);

            // draw vulnerable target
            if (_knockbackTarget != pc && _knockbackTarget != target)
                arena.Actor(_knockbackTarget, arena.ColorVulnerable);
        }

        public override void OnEventCast(CastEvent info)
        {
            if (info.IsSpell(AID.TrueHoly2) || info.IsSpell(AID.TrueFlare2))
                AOEDone = true;
        }

        // we assume that if boss target is the same as knockback target, it's a tank using invul, and so raid shouldn't stack
        private bool RaidShouldStack(Actor bossTarget) =>  !_isFlare && _knockbackTarget != bossTarget;
    }
}
