namespace UnderPressure.PowerGrid
{
    internal static class ElectricObjectCatalog
    {
        // PREFAB | TIPO DE COBRO | TIPO DE RED | ALTURA DEL NÚMERO
        internal static readonly (string Prefab, Cost Cobro, Power Red, float Altura)[] Objects =
        {
            (ReceptionNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (WardNurseStationNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (OfficeDeskNativeParameters.Prefab, Cost.Mensual, Power.Bajo, -0.4f),
            (VendingMachineDrinksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineEnergyDrinksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineLuxuryDrinksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineGambleDrinksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineLaxativeDrinksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineSnacksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineSaltySnacksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineLuxurySnacksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineSpongeSnacksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineToySnacksNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineCaviarSnackNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineLemonNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineTomatoesNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineCarrotNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineWaterNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (VendingMachineRetroKebabNativeParameters.Prefab, Cost.Mensual, Power.Bajo, 0f),
            (ToiletHandDryerNativeParameters.Prefab, Cost.Tarea, Power.Alto, 0f),
            (ToiletHandDryerGoldNativeParameters.Prefab, Cost.Tarea, Power.Alto, 0f),
            (DiagnosisMachineNativeParameters.Prefab, Cost.Tarea, Power.Alto, -3f),
            (LightHeadedMachineNativeParameters.Prefab, Cost.Tarea, Power.Alto, -2f),
            (DrugDispenserNativeParameters.Prefab, Cost.Tarea, Power.Alto, -2f),
            (MachineControlConsoleNativeParameters.Prefab, Cost.Tarea, Power.Alto, -0.1f)
        };
    }
}
