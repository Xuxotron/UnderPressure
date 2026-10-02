namespace UnderPressure.PowerGrid
{
    internal static class ElectricObjectCatalog
    {
        // PREFAB | TIPO DE COBRO | TIPO DE RED | ALTURA DEL NÚMERO
        internal static readonly (string Prefab, Cost Cobro, Power Red, float Altura)[] Objects =
        {
            (RoomItemDefinitionCatalog.Reception.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.WardNurseStation.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.OfficeDesk.Prefab, Cost.Mensual, Power.Bajo, -0.4f),
            (RoomItemDefinitionCatalog.VendingMachineDrinks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineEnergyDrinks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineLuxuryDrinks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineGambleDrinks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineLaxativeDrinks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineSnacks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineSaltySnacks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineLuxurySnacks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineSpongeSnacks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineToySnacks.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineCaviarSnack.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineLemon.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineTomatoes.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineCarrot.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineWater.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.VendingMachineRetroKebab.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (RoomItemDefinitionCatalog.ToiletHandDryer.Prefab, Cost.Tarea, Power.Alto, 0f),
            (RoomItemDefinitionCatalog.ToiletHandDryerGold.Prefab, Cost.Tarea, Power.Alto, 0f),
            (RoomItemDefinitionCatalog.DiagnosisMachine.Prefab, Cost.Tarea, Power.Alto, -3f),
            (RoomItemDefinitionCatalog.LightHeadedMachine.Prefab, Cost.Tarea, Power.Alto, -2f),
            (RoomItemDefinitionCatalog.DrugDispenser.Prefab, Cost.Tarea, Power.Alto, -2f),
            (RoomItemDefinitionCatalog.MachineControlConsole.Prefab, Cost.Tarea, Power.Alto, -0.1f)
        };
    }
}
