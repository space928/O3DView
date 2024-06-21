using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace O3DParse.Ini;

/// <summary>
/// Represents a passenger cabin file used by a bus.
/// </summary>
[Serializable]
[OmsiIniCommand("passengercabin")]
public class OmsiPassengerCabin
{
    [OmsiIniCommandFile] public string path = "";

    public EntryCommand[] entries = [];
    public ExitCommand[] exits = [];
    public LinkToNextVehCommand? linkToNextVeh;
    public LinkToPrevVehCommand? linkToPrevVeh;
    public StamperCommand? stamper;
    public TicketSaleCommand? ticketSale;
    public TicketSaleMoneyPoint2Command? ticketSaleMoneyPoint;
    public TicketSaleChangePoint2Command? ticketSaleChangePoint;
    public SeatCommand[] seats = [];

    [OmsiIniComments] public string? comments;
    [OmsiIniComments(true)] public string? postComments;
}

[OmsiIniCommand("entry")]
public struct EntryCommand
{
    public int pathPoint;

    public NoTicketSaleCommand? noTicketSale;
    public WithButtonCommand? withButton;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("{noticketsale}")]
[OmsiIniVerbatimCommand]
public struct NoTicketSaleCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("{withbutton}")]
[OmsiIniVerbatimCommand]
public struct WithButtonCommand
{
    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("exit")]
public struct ExitCommand
{
    public int pathPoint;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("linkToNextVeh")]
public struct LinkToNextVehCommand
{
    public int pathPoint;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("linkToPrevVeh")]
public struct LinkToPrevVehCommand
{
    public int pathPoint;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("stamper")]
public struct StamperCommand
{
    public int pathPoint;
    public Vector3 pos;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("ticket_sale")]
public struct TicketSaleCommand
{
    public int pathPoint;
    public Vector3 pos;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("ticket_sale_money_point")]
public struct TicketSaleMoneyPointCommand
{
    public Vector3 moneyPos;
    public Vector2 variation;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("ticket_sale_money_point_2")]
[OmsiIniDerivedCommand<TicketSaleMoneyPointCommand>]
public struct TicketSaleMoneyPoint2Command
{
    public Vector3 moneyPos;
    public Vector2 variation;
    public string parent;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("ticket_sale_change_point")]
public struct TicketSaleChangePointCommand
{
    public Vector3 moneyPos;
    public Vector2 variation;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("ticket_sale_change_point_2")]
[OmsiIniDerivedCommand<TicketSaleChangePointCommand>]
public struct TicketSaleChangePoint2Command
{
    public Vector3 moneyPos;
    public Vector2 variation;
    public string parent;

    [OmsiIniComments] public string comments;
}

[OmsiIniCommand("___seat")]
[OmsiIniDerivedCommand<PassengerPosCommand>]
[OmsiIniDerivedCommand<DriverPosCommand>]
public abstract class SeatCommand
{
    public Vector3 position;
    public float height;
    public float rotation;

    public IlluminationInteriorCommand? illuminationInterior;

    [OmsiIniComments] public string? comments;
}

[OmsiIniCommand("passpos")]
public class PassengerPosCommand : SeatCommand
{

}

[OmsiIniCommand("drivpos")]
public class DriverPosCommand : SeatCommand
{

}
