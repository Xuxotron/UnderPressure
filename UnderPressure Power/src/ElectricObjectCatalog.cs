namespace UnderPressure.PowerGrid
{
    internal static class ElectricObjectCatalog
    {
        // PREFAB | CONSUMO | TIPO DE COBRO | TIPO DE RED | ALTURA DEL NÚMERO
        internal static readonly (string Prefab, int Consumo, Cost Cobro, Power Red, float Altura)[] Objects =
        {
            ("powerpanel", 0, Cost.Mensual, Power.Bajo, 0f),
            ("RI_Reception", 100, Cost.Mensual, Power.Bajo, 0f),
            ("RI_Ward_Nurse_Station", 100, Cost.Mensual, Power.Bajo, 0f),
            ("RI_OfficeDesk", 100, Cost.Mensual, Power.Bajo, -0.4f),
            ("RI_VendingMachine_Drinks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_EnergyDrinks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_LuxuryDrinks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_GambleDrinks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_Drinks_Laxative", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_Snacks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_SaltySnacks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_LuxurySnacks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_SpongeSnacks", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_Snacks_Toy", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_VendingMachine_CaviarSnack", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_Grid_Vending_Lemon_V1", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_Grid_Vending_Tomatoes_V1", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_Grid_Vending_Carrot_V1", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_Grid_Vending_Water_V1", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_IP_Retro_Kebab_Vending_Machine_V1", 50, Cost.Mensual, Power.Bajo, 0f),
            ("RI_ToiletHandDryer", 5, Cost.Tarea, Power.Alto, 0f),
            ("RI_ToiletHandDryer_Gold", 5, Cost.Tarea, Power.Alto, 0f),
            ("RI_DiagnosisMachine", 150, Cost.Tarea, Power.Alto, -3f),
            ("RI_Machine_LightHeaded", 350, Cost.Tarea, Power.Alto, -2f),
            ("RI_DrugDispenser", 200, Cost.Tarea, Power.Alto, -2f),
            ("RI_Machine_Control_Console", 10, Cost.Tarea, Power.Alto, -0.1f)
        };
    }
}
