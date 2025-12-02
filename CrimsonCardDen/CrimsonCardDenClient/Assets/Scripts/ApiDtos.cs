using System;
using System.Collections.Generic;

/// <summary>
/// DTOs shared between BackendClient and GameController.
/// No namespace to keep things simple in Unity.
/// </summary>

[Serializable]
public class CardDto
{
    public bool isJoker;
    public string suit;
    public string rank;
}

[Serializable]
public class HandDto
{
    public int seatIndex;
    public List<CardDto> cards;
}

[Serializable]
public class BidDto
{
    public int seatIndex;
    public int tricks;
    public string trump;
    public int strength;
}

[Serializable]
public class BiddingStateDto
{
    public int dealerSeatIndex;
    public int currentSeatIndex;
    public BidDto currentHighBid;
    public bool isCompleted;
    public List<BidDto> actions;
}

[Serializable]
public class TrickPlayDto
{
    public int seatIndex;
    public CardDto card;
}

[Serializable]
public class TrickDto
{
    public int trickNumber;
    public List<TrickPlayDto> plays;
    public int winnerSeatIndex;
}

[Serializable]
public class SessionResponse
{
    public string id;
    public string phase;                // "Bidding", "Kitty", "Playing", "Completed"
    public int playerCount;
    public List<HandDto> hands;
    public BiddingStateDto bidding;

    public int? currentSeatToPlay;      // nullable
    public int? contractSeatIndex;
    public int? contractTricks;
    public string contractTrump;
    public bool? contractMade;

    public int tricksWonByContractTeam;
    public int tricksWonByDefence;
    public int teamScoreA;
    public int teamScoreB;

    // Completed tricks from backend
    public List<TrickDto> tricks;
}

[Serializable]
public class DealResponse
{
    public List<HandDto> hands;
}
