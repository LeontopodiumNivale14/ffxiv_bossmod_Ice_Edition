namespace BossMod.Modules.Stormblood.DeepDungeon.D70;

public enum OID : uint
{
    Boss = 0x23EB, // R6.000, x1
    Actor1e86e0 = 0x1E86E0, // R2.000, x1, EventObj type
    Actor1e9829 = 0x1E9829, // R0.500, x0 (spawn during fight), EventObj type
}

public enum AID : uint
{
    AutoAttack = 6497, // Boss->player, no cast, single-target
    Devour = 12204, // Boss->location, no cast, range 4+R ?-degree cone
    HoundOutOfHell = 12206, // Boss->player, 5.0s cast, width 14 rect charge
    Innerspace = 12207, // Boss->player, 3.0s cast, single-target, not necessary?
    PredatorClaws = 12205, // Boss->self, 3.0s cast, range 9+R(15) 90?-degree cone, done
    Slabber = 12203, // Boss->location, 3.0s cast, range 8 circle, done
    Ululation = 12208, // Boss->self, 3.0s cast, range 80+R circle, done
}

class Devour(BossModule module) : BossComponent(module);

class HoundOutOfHell(BossModule module) : Components.BaitAwayChargeCast(module, ActionID.MakeSpell(AID.HoundOutOfHell), 7);
// needs to be heavily edited/things to do:
// 1: Make it to where it's shown as a void zone while it's up/the boss is not casting devour/hound out of hell
// 2: If hound out of hell is being cast, then make it a tower for the person being targeted with? (Need to doublecheck/make sure this is how it works
// 3: After Devour is cast, change puddle back to void-zone

class PredatorClaws(BossModule module) : Components.SelfTargetedAOEs(module, ActionID.MakeSpell(AID.PredatorClaws), new AOEShapeCone(15, 45.Degrees()));
class Slabber(BossModule module) : Components.LocationTargetedAOEs(module, ActionID.MakeSpell(AID.Slabber), 8);
class Ululation(BossModule module) : Components.RaidwideCast(module, ActionID.MakeSpell(AID.Ululation), "Raidwide, be out of the puddle!");

class D70KenkoStates : StateMachineBuilder
{
    public D70KenkoStates(BossModule module) : base(module)
    {
        TrivialPhase();
    }
}

[ModuleInfo(BossModuleInfo.Maturity.WIP, GroupType = BossModuleInfo.GroupType.CFC, GroupID = 546, NameID = 7489)]
public class D70Kenko(WorldState ws, Actor primary) : BossModule(ws, primary, new(-300, -300), new ArenaBoundsCircle(25f));
