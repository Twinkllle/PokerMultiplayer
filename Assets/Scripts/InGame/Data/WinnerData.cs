using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WinnerData
{
    public readonly Player Winner;
    public readonly uint Chips;

    public WinnerData(Player winner, uint chips)
    {
        Winner = winner;
        Chips = chips;
    }
}