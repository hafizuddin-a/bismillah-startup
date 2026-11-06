using System.Windows;

namespace Bismillah;

/// <summary>Whether to skip the animations (Windows "animation effects" off). CI forces them on to test the animated path.</summary>
internal static class Motion
{
    public static bool ForceAnimation { get; set; }
    public static bool Reduce => !ForceAnimation && !SystemParameters.ClientAreaAnimation;
}
