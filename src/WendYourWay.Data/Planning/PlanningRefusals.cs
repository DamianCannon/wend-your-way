namespace WendYourWay.Data.Planning;

/// <summary>A GPX route that can't be shown on the Network (T083): the message says why, never where.</summary>
public sealed class RouteNotMatchedException(string message) : Exception(message);

/// <summary>A Point-to-point that can't be planned (T090): the message says why.</summary>
public sealed class PointToPointRefusedException(string message) : Exception(message);

/// <summary>A Request starting outside England and Wales, where Wend Your Way plans nothing (R1.2).</summary>
public sealed class OutsideEnglandAndWalesException()
    : Exception("That start is outside England and Wales: Wend Your Way plans Loops only there.");
