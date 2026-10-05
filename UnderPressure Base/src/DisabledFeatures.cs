// Purpose: Centralizes switches that disable unfinished features without removing their code.
namespace UnderPressure
{
    /// <summary>
    /// Interruptores provisionales para excluir funciones incompletas de las versiones publicadas.
    /// Cambiar un valor a false reactiva su implementación sin recuperar código eliminado.
    /// </summary>
    public static class DisabledFeatures
    {
        public static readonly bool EnergyCampaignLaunch = true;
        public static readonly bool EnergyRoomDesk = true;
    }
}
