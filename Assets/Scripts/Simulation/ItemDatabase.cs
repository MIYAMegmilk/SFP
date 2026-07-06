using System.Collections.Generic;

namespace SFP.Simulation
{
    public enum ItemId
    {
        None,
        // Materials
        SteelBar,
        Copper,
        Silicon,
        Rubber,
        Plastic,
        // Tools
        WeldingTool,
        Wrench,
        Crowbar,
        Screwdriver,
        Wire,
        // Equipment
        DivingSuit,
        OxygenTank,
        Flashlight,
        FireExtinguisher,
        // Medical
        Bandage,
        Morphine,
        Antibiotics,
        BloodPack,
        // Ammo
        CoilgunAmmo,
        RailgunSlug,
        // Fuel
        FuelRod,
    }

    public sealed class ItemRecipe
    {
        public ItemId Output;
        public int OutputCount = 1;
        public Dictionary<ItemId, int> Inputs;
        public float CraftTime;
    }

    public static class ItemDatabase
    {
        public static readonly ItemRecipe[] FabricatorRecipes =
        {
            new() { Output = ItemId.WeldingTool, CraftTime = 5f,
                Inputs = new() { { ItemId.SteelBar, 1 }, { ItemId.Copper, 1 } } },
            new() { Output = ItemId.Wire, OutputCount = 3, CraftTime = 3f,
                Inputs = new() { { ItemId.Copper, 1 } } },
            new() { Output = ItemId.DivingSuit, CraftTime = 10f,
                Inputs = new() { { ItemId.Rubber, 2 }, { ItemId.Plastic, 1 }, { ItemId.SteelBar, 1 } } },
            new() { Output = ItemId.OxygenTank, CraftTime = 6f,
                Inputs = new() { { ItemId.SteelBar, 2 } } },
            new() { Output = ItemId.Flashlight, CraftTime = 4f,
                Inputs = new() { { ItemId.Plastic, 1 }, { ItemId.Copper, 1 } } },
            new() { Output = ItemId.FireExtinguisher, CraftTime = 5f,
                Inputs = new() { { ItemId.SteelBar, 1 }, { ItemId.Rubber, 1 } } },
            new() { Output = ItemId.CoilgunAmmo, OutputCount = 4, CraftTime = 4f,
                Inputs = new() { { ItemId.SteelBar, 1 } } },
            new() { Output = ItemId.FuelRod, CraftTime = 8f,
                Inputs = new() { { ItemId.SteelBar, 1 }, { ItemId.Silicon, 1 } } },
        };

        public static readonly ItemRecipe[] MedicalRecipes =
        {
            new() { Output = ItemId.Bandage, OutputCount = 2, CraftTime = 3f,
                Inputs = new() { { ItemId.Rubber, 1 } } },
            new() { Output = ItemId.Morphine, CraftTime = 5f,
                Inputs = new() { { ItemId.Plastic, 1 } } },
            new() { Output = ItemId.Antibiotics, CraftTime = 6f,
                Inputs = new() { { ItemId.Plastic, 1 }, { ItemId.Silicon, 1 } } },
            new() { Output = ItemId.BloodPack, CraftTime = 4f,
                Inputs = new() { { ItemId.Rubber, 1 }, { ItemId.Plastic, 1 } } },
        };

        public static readonly Dictionary<ItemId, Dictionary<ItemId, int>> DeconstructOutputs = new()
        {
            { ItemId.WeldingTool, new() { { ItemId.SteelBar, 1 } } },
            { ItemId.DivingSuit, new() { { ItemId.Rubber, 1 }, { ItemId.Plastic, 1 } } },
            { ItemId.Flashlight, new() { { ItemId.Plastic, 1 } } },
            { ItemId.FireExtinguisher, new() { { ItemId.SteelBar, 1 } } },
        };

        public static string GetDisplayName(ItemId id)
        {
            switch (id)
            {
                case ItemId.SteelBar: return "Steel Bar";
                case ItemId.Copper: return "Copper";
                case ItemId.Silicon: return "Silicon";
                case ItemId.Rubber: return "Rubber";
                case ItemId.Plastic: return "Plastic";
                case ItemId.WeldingTool: return "Welding Tool";
                case ItemId.Wrench: return "Wrench";
                case ItemId.Crowbar: return "Crowbar";
                case ItemId.Screwdriver: return "Screwdriver";
                case ItemId.Wire: return "Wire";
                case ItemId.DivingSuit: return "Diving Suit";
                case ItemId.OxygenTank: return "O2 Tank";
                case ItemId.Flashlight: return "Flashlight";
                case ItemId.FireExtinguisher: return "Extinguisher";
                case ItemId.Bandage: return "Bandage";
                case ItemId.Morphine: return "Morphine";
                case ItemId.Antibiotics: return "Antibiotics";
                case ItemId.BloodPack: return "Blood Pack";
                case ItemId.CoilgunAmmo: return "Coilgun Ammo";
                case ItemId.RailgunSlug: return "Railgun Slug";
                case ItemId.FuelRod: return "Fuel Rod";
                default: return id.ToString();
            }
        }
    }
}
