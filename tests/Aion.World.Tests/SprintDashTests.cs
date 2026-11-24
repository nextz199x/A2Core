using Aion.Protocol.Native;
using Aion.World;
using Xunit;

namespace Aion.World.Tests;

/// <summary>Shift sprint (0x3704): official opens with NormalDodge dash; standalone dodge spam still gets 9-byte unlock.</summary>
public sealed class SprintDashTests
{
    static readonly long Now = 1_760_004_000_000;

    static byte[] SprintOn() => new PacketWriter().U16(0x3704).I64(1).ToArray();

    static byte[] DodgeCast() =>
        new PacketWriter().U16(0x3800).U16(16).I32(GameSession.CombatDodge).U8(2).Varint(1).F32(0)
            .Vec3(new Vector3d(100, 0, 0)).U8(9).ToArray();

    static int[] Opcodes(IEnumerable<byte[]> packets) => packets.Select(p => (int)GameSessionTestHelpers.Op(p)).ToArray();

    [Fact]
    public async Task Sprint_on_then_auto_combat_dodge_matches_the_official_batch()
    {
        var store = new FakeCharacterStore { Progress = (10, 0) };
        var session = await GameSessionTestHelpers.EnterWorldAsync(store, Now, level: 10);
        Assert.Equal(7, session.DashVariant);
        var spBefore = session.Resource!.Sp;
        var on = await session.HandleAsync(SprintOn(), Now);
        Assert.Empty(on);   // held for the dash's write, as the official server answered
        var dash = await session.HandleAsync(DodgeCast(), Now + 4);
        Assert.Equal([0x8d03, 0x3741, 0x3802, 0x3801, 0x382a, 0x8d00], Opcodes(dash));
        Assert.Equal(GameSession.NormalDodge, BitConverter.ToInt32(dash[2], 4));
        Assert.Equal(spBefore - GameSession.NormalDodgeSp, session.Resource.Sp);
    }

    [Fact]
    public async Task Sprint_on_then_dash_keeps_the_legacy_variant_0_reply_for_live_tests()
    {
        var store = new FakeCharacterStore { Progress = (10, 0) };
        var session = await GameSessionTestHelpers.EnterWorldAsync(store, Now, level: 10);
        session.DashVariant = 0;
        var on = await session.HandleAsync(SprintOn(), Now);
        Assert.Equal([0x8d03, 0x3741], Opcodes(on));
        var dash = await session.HandleAsync(DodgeCast(), Now + 4);
        Assert.Equal([0x3801, 0x8d00, 0x3802], Opcodes(dash));
    }

    [Fact]
    public async Task Sprint_auto_dodge_with_low_sp_still_sends_dodge_unlock_refusal()
    {
        var store = new FakeCharacterStore { Progress = (10, 0) };
        var session = await GameSessionTestHelpers.EnterWorldAsync(store, Now, level: 10);
        session.SetResource('S', GameSession.NormalDodgeSp - 1);
        var spBefore = session.Resource!.Sp;
        await session.HandleAsync(SprintOn(), Now);
        var dash = await session.HandleAsync(DodgeCast(), Now + 4);
        Assert.Equal([0x8d03, 0x3741, 0x3801], Opcodes(dash));
        Assert.Equal(GamePackets.DodgeResult(GameSession.CombatDodge, 1), dash[^1]);
        Assert.Equal(spBefore, session.Resource.Sp);
    }

    [Fact]
    public async Task Rapid_standalone_dodge_resends_while_dodging_are_ignored_not_refused()
    {
        var store = new FakeCharacterStore { Progress = (10, 0) };
        var session = await GameSessionTestHelpers.EnterWorldAsync(store, Now, level: 10);
        var first = await session.HandleAsync(DodgeCast(), Now);
        Assert.Contains(first, p => GameSessionTestHelpers.Op(p) == 0x3802);
        for (var i = 1; i < 8; i++)
        {
            var retry = await session.HandleAsync(DodgeCast(), Now + i * 4);
            Assert.Empty(retry); // the 9-byte refusal mid-dash froze the client (live 2026-11-02)
        }
    }

    [Fact]
    public async Task Variant_7_batches_the_sprint_with_the_dash_like_the_official_capture()
    {
        // Official 2026-11-24 06:02:00.802 (capture 20261124-130131 #1415-1421), all in one write:
        // 0x8d03, 0x3741, 0x3802, 0x3801, 0x382a, 0x8d00.
        var store = new FakeCharacterStore { Progress = (10, 0) };
        var session = await GameSessionTestHelpers.EnterWorldAsync(store, Now, level: 10);
        session.DashVariant = 7;
        var on = await session.HandleAsync(SprintOn(), Now);
        Assert.Empty(on);
        var dash = await session.HandleAsync(DodgeCast(), Now + 4);
        Assert.Equal([0x8d03, 0x3741, 0x3802, 0x3801, 0x382a, 0x8d00], Opcodes(dash));
    }

    [Fact]
    public async Task Legacy_dash_variant_10_still_accepts_sprint_opening_dodge_not_refusal()
    {
        var store = new FakeCharacterStore { Progress = (10, 0) };
        var session = await GameSessionTestHelpers.EnterWorldAsync(store, Now, level: 10);
        session.DashVariant = 10;
        await session.HandleAsync(SprintOn(), Now);
        var dash = await session.HandleAsync(DodgeCast(), Now + 4);
        Assert.Equal([0x3801, 0x8d00, 0x3802], Opcodes(dash));
    }
}
