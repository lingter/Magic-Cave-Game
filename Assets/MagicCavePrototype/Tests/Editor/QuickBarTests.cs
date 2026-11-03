using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public sealed class QuickBarTests
{
    static RunData New()
    {
        var run = new RunData { health = 20, maxHealth = 60 };
        for (int i = 0; i < 5; i++) run.inventory.Add(new InventorySlot());
        return run;
    }
    static void Pick(RunData run, params ItemKind[] kinds)
    {
        var loot = new GroundLoot { loose = true };
        foreach (var kind in kinds) loot.units.Add(new ItemUnit { id = run.nextItemId++, kind = kind });
        Inventory.Pickup(run, loot);
    }
    [Test]
    public void PickupFillsThreeDistinctKindsWithoutExtraInventory()
    {
        var run = New();
        Pick(run, ItemKind.Ore, ItemKind.Potion, ItemKind.Potion, ItemKind.Torch, ItemKind.StrengthEssence, ItemKind.AssaultPotion);
        CollectionAssert.AreEqual(new[] { (int)ItemKind.Potion, (int)ItemKind.Torch, (int)ItemKind.StrengthEssence }, run.quickSlots);
        Assert.That(QuickBar.Count(run, ItemKind.Potion), Is.EqualTo(2));
        Assert.That(run.inventory.Sum(s => s.units.Count), Is.EqualTo(6));
    }
    [Test]
    public void BindingFollowsBagMovesAndLastUseClearsWithoutRefilling()
    {
        var run = New(); Pick(run, ItemKind.Potion, ItemKind.Torch, ItemKind.StrengthEssence, ItemKind.AssaultPotion);
        Inventory.MoveSlot(run, 0, 4);
        Assert.That(QuickBar.BagSlot(run, 0), Is.EqualTo(4));
        Assert.That(Inventory.Use(run, 4), Is.True); QuickBar.Ensure(run);
        Assert.That(run.quickSlots[0], Is.EqualTo(-1));
        Pick(run, ItemKind.AssaultPotion);
        Assert.That(run.quickSlots[0], Is.EqualTo((int)ItemKind.AssaultPotion));
    }
    [Test]
    public void FullBagFailedPickupNeverBindsAndFullHealthNeverConsumes()
    {
        var run = New(); Pick(run, ItemKind.Potion); run.health = 60;
        Assert.That(Inventory.Use(run, QuickBar.BagSlot(run, 0)), Is.False);
        for (int i = 1; i < run.inventory.Count; i++) run.inventory[i].units.Add(new ItemUnit { kind = ItemKind.IronSword });
        Pick(run, ItemKind.Torch);
        Assert.That(run.quickSlots.Contains((int)ItemKind.Torch), Is.False);
        Assert.That(QuickBar.Count(run, ItemKind.Potion), Is.EqualTo(1));
    }
    [Test]
    public void BurningTorchKeepsShortcutWithoutStockAndExpiresAfterTwentyMoves()
    {
        var run = MapGenerator.NewRun(12345, AttributeKind.None, false);
        Pick(run, ItemKind.Torch);
        int index = System.Array.IndexOf(run.quickSlots, (int)ItemKind.Torch);
        Assert.That(Inventory.Use(run, QuickBar.BagSlot(run, index)), Is.True);
        QuickBar.Ensure(run);
        Assert.That(QuickBar.Count(run, ItemKind.Torch), Is.Zero);
        Assert.That(QuickBar.IsBurningTorch(run, index), Is.True);
        var copy = JsonUtility.FromJson<RunData>(JsonUtility.ToJson(run)); QuickBar.Ensure(copy);
        Assert.That(QuickBar.IsBurningTorch(copy, index), Is.True);
        Assert.That(TorchLight.Extinguish(copy), Is.True); QuickBar.Ensure(copy);
        Assert.That(copy.quickSlots[index], Is.EqualTo((int)ItemKind.Torch));
        Assert.That(QuickBar.BagSlot(copy, index), Is.EqualTo(-1));
        Assert.That(QuickBar.HasTorch(copy, index), Is.True);
        TorchLight.Step(copy);
        Assert.That(copy.torchStepsRemaining, Is.EqualTo(20));
        Assert.That(TorchLight.Resume(copy), Is.True);
        Assert.That(QuickBar.IsBurningTorch(copy, index), Is.True);
        for (int step = 0; step < 20; step++) TorchLight.Step(run);
        QuickBar.Ensure(run);
        Assert.That(run.quickSlots[index], Is.EqualTo(-1));
    }
    [Test]
    public void AssignmentAndSaveRoundTripKeepClearedSlotAndRejectEquipment()
    {
        var run = New(); Pick(run, ItemKind.Potion, ItemKind.Torch);
        Assert.That(QuickBar.Assign(run, 2, ItemKind.Potion), Is.True);
        Assert.That(QuickBar.Assign(run, 0, ItemKind.IronSword), Is.False);
        var copy = JsonUtility.FromJson<RunData>(JsonUtility.ToJson(run)); QuickBar.Ensure(copy);
        CollectionAssert.AreEqual(new[] { -1, (int)ItemKind.Torch, (int)ItemKind.Potion }, copy.quickSlots);
        copy.quickSlots = null; QuickBar.Ensure(copy);
        Assert.That(copy.quickSlots[0], Is.EqualTo((int)ItemKind.Potion));
    }
}
