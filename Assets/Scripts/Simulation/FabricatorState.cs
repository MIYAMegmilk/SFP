namespace SFP.Simulation
{
    public sealed class FabricatorState
    {
        public int PowerNodeId = -1;
        public float PowerConsumption = 80f;
        public bool IsMedical;

        public ItemRecipe CurrentRecipe;
        public float CraftProgress;
        public bool IsCrafting;

        public InventoryState InputInventory = new InventoryState(10);
        public InventoryState OutputInventory = new InventoryState(10);

        public float CraftFraction => CurrentRecipe != null && CurrentRecipe.CraftTime > 0f
            ? CraftProgress / CurrentRecipe.CraftTime : 0f;

        public bool CanStartCraft(ItemRecipe recipe)
        {
            return recipe != null
                && InputInventory.HasAll(recipe.Inputs)
                && OutputInventory.HasSpace(recipe.OutputCount);
        }

        public void StartCraft(ItemRecipe recipe)
        {
            if (!CanStartCraft(recipe)) return;
            InputInventory.RemoveAll(recipe.Inputs);
            CurrentRecipe = recipe;
            CraftProgress = 0f;
            IsCrafting = true;
        }

        public void Tick(float dt, PowerGrid power)
        {
            if (!IsCrafting || CurrentRecipe == null) return;

            if (PowerNodeId >= 0 && power != null)
            {
                var node = power.GetNode(PowerNodeId);
                if (node == null || !node.IsActive) return;
            }

            float voltageScale = power != null
                ? (power.GridVoltage > 1f ? 1f : power.GridVoltage) : 1f;
            CraftProgress += dt * voltageScale;

            if (CraftProgress >= CurrentRecipe.CraftTime)
            {
                OutputInventory.Add(CurrentRecipe.Output, CurrentRecipe.OutputCount);
                IsCrafting = false;
                CurrentRecipe = null;
                CraftProgress = 0f;
            }
        }
    }
}
