using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameController : MonoBehaviour
{
    [Header("Backend")]
    [SerializeField] private BackendClient backendClient;

    [Header("Deal demo (optional, old endpoint)")]
    [SerializeField] private Button dealButton;
    [SerializeField] private TMP_Text dealHandText;

    [Header("Local 4-player session")]
    [SerializeField] private Button hostSessionButton;

    [Header("Hand control (player = seat 0)")]
    [SerializeField] private TMP_InputField cardIndexInput;
    [SerializeField] private Button sortHandButton;

    [Header("Bidding UI (player = seat 0)")]
    [SerializeField] private TMP_Dropdown tricksDropdown;      // options: 6,7,8,9,10
    [SerializeField] private TMP_Dropdown trumpDropdown;       // options: Clubs, Diamonds, Hearts, Spades, NoTrump
    [SerializeField] private Button submitBidButton;
    [SerializeField] private Button passButton;

    [Header("Kitty / play / scoring / match")]
    [SerializeField] private Button revealKittyButton;   // mostly debug, auto-used too
    [SerializeField] private Button playCardButton;      // plays selected card for seat 0
    [SerializeField] private Button nextHandButton;      // starts a new hand, keeps match scores
    [SerializeField] private TMP_Text handSeat0Text;
    [SerializeField] private TMP_Text sessionInfoText;

    private string currentSessionId;
    private SessionResponse currentSession;

    private const int PlayerSeatIndex = 0; // local player is always seat 0

    // Simple client-side match scores (sum of hand scores)
    private int matchScoreA = 0;
    private int matchScoreB = 0;
    private bool currentHandScored = false;

    // Track if the player has already passed this hand
    private bool playerHasPassedThisHand = false;

    private readonly string[] trumpOrder = { "Clubs", "Diamonds", "Hearts", "Spades", "NoTrump" };

    // For sorting the hand
    private readonly Dictionary<string, int> suitSortOrder = new()
    {
        { "Clubs", 0 },
        { "Diamonds", 1 },
        { "Hearts", 2 },
        { "Spades", 3 }
    };

    // Ascending order; we’ll sort *descending* for A-high
    private readonly Dictionary<string, int> rankSortOrder = new()
    {
        { "Four", 0 },
        { "Five", 1 },
        { "Six", 2 },
        { "Seven", 3 },
        { "Eight", 4 },
        { "Nine", 5 },
        { "Ten", 6 },
        { "Jack", 7 },
        { "Queen", 8 },
        { "King", 9 },
        { "Ace", 10 }
    };

    private void Awake()
    {
        if (dealButton != null)
            dealButton.onClick.AddListener(OnDealClicked);

        if (hostSessionButton != null)
            hostSessionButton.onClick.AddListener(OnHostSessionClicked);

        if (submitBidButton != null)
            submitBidButton.onClick.AddListener(OnSubmitBidClicked);

        if (passButton != null)
            passButton.onClick.AddListener(OnPassClicked);

        if (revealKittyButton != null)
            revealKittyButton.onClick.AddListener(OnRevealKittyClicked);

        if (playCardButton != null)
            playCardButton.onClick.AddListener(OnPlayPlayerCardClicked);

        if (nextHandButton != null)
            nextHandButton.onClick.AddListener(OnNextHandClicked);

        if (sortHandButton != null)
            sortHandButton.onClick.AddListener(OnSortHandClicked);

        if (cardIndexInput != null && string.IsNullOrWhiteSpace(cardIndexInput.text))
            cardIndexInput.text = "0";

        // Populate dropdowns if empty (otherwise use whatever you set up in the Editor)
        if (tricksDropdown != null && tricksDropdown.options.Count == 0)
        {
            tricksDropdown.options = new List<TMP_Dropdown.OptionData>
            {
                new("6"), new("7"), new("8"), new("9"), new("10")
            };
            tricksDropdown.value = 0;
        }

        if (trumpDropdown != null && trumpDropdown.options.Count == 0)
        {
            trumpDropdown.options = trumpOrder
                .Select(s => new TMP_Dropdown.OptionData(s))
                .ToList();
            trumpDropdown.value = 2; // Hearts by default
        }
    }

    // ---------- Old simple deal demo (still useful for debugging) ----------

    private async void OnDealClicked()
    {
        if (dealHandText == null) return;

        dealHandText.text = "Dealing...";
        try
        {
            var deal = await backendClient.HostLocalDealAsync(4);

            var seat0 = deal.hands.FirstOrDefault(h => h.seatIndex == 0);
            if (seat0 == null || seat0.cards == null)
            {
                dealHandText.text = "No hand for seat 0.";
                return;
            }

            var lines = seat0.cards.Select(CardToString);
            dealHandText.text = "Your hand (deal endpoint):\n" + string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            dealHandText.text = "Error dealing hand. See console.";
        }
    }

    // ---------- Session / match control ----------

    private async void OnHostSessionClicked()
    {
        if (sessionInfoText != null)
            sessionInfoText.text = "Creating new hand...";

        currentHandScored = false;
        playerHasPassedThisHand = false;

        try
        {
            currentSession = await backendClient.HostLocalSessionAsync(4);
            currentSessionId = currentSession.id;

            UpdateUiFromSession("New hand created. Phase = Bidding.");

            // Let bots bid until it's your turn or bidding finishes
            await AutoBidForBotsAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            if (sessionInfoText != null)
                sessionInfoText.text = "Error creating session.";
        }
    }

    private void OnNextHandClicked()
    {
        // Start a new hand; matchScoreA/B are kept
        OnHostSessionClicked();
    }

    // ---------- Player bidding ----------

    public async void OnSubmitBidClicked()
    {
        if (!EnsureSession()) return;
        if (backendClient == null)
        {
            Debug.LogError("[GameController] BackendClient is not assigned.");
            return;
        }

        if (currentSession.phase != "Bidding" || currentSession.bidding == null)
        {
            if (sessionInfoText != null)
                sessionInfoText.text = $"Cannot bid: phase is {currentSession.phase}.";
            return;
        }

        // If it's not your turn yet, let AI act until it is (or bidding ends)
        if (currentSession.bidding.currentSeatIndex != PlayerSeatIndex)
        {
            await AutoBidForBotsAsync();

            if (currentSession.phase != "Bidding" ||
                currentSession.bidding == null ||
                currentSession.bidding.currentSeatIndex != PlayerSeatIndex)
            {
                if (sessionInfoText != null)
                    sessionInfoText.text = "Still not your turn to bid.";
                return;
            }
        }

        int tricks = GetSelectedTricks();
        string trump = GetSelectedTrump();

        try
        {
            currentSession = await backendClient.PlaceBidAsync(
                currentSessionId,
                PlayerSeatIndex,
                tricks,
                trump);

            playerHasPassedThisHand = false; // you’ve made a live bid
            UpdateUiFromSession($"Player (seat 0) bid {tricks} {trump}.");

            // Let AI continue bidding until it’s your turn again or bidding ends
            await AutoBidForBotsAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            if (sessionInfoText != null)
                sessionInfoText.text = $"Bid failed: {ex.Message}";
        }
    }

    private async void OnPassClicked()
    {
        if (!EnsureSession()) return;

        if (currentSession.phase != "Bidding")
        {
            if (sessionInfoText != null)
                sessionInfoText.text = $"Cannot pass: phase is {currentSession.phase}.";
            return;
        }

        if (currentSession.bidding == null)
        {
            if (sessionInfoText != null)
                sessionInfoText.text = "Bidding state is missing.";
            return;
        }

        // If you've already passed this hand, don't pass again.
        if (playerHasPassedThisHand)
        {
            if (sessionInfoText != null)
                sessionInfoText.text = "You have already passed this hand.";
            return;
        }

        // If it's not your turn, let AI handle their bids until it is.
        if (currentSession.bidding.currentSeatIndex != PlayerSeatIndex)
        {
            await AutoBidForBotsAsync();

            if (currentSession.phase != "Bidding" ||
                currentSession.bidding == null ||
                currentSession.bidding.currentSeatIndex != PlayerSeatIndex)
            {
                if (sessionInfoText != null)
                    sessionInfoText.text = "Still not your turn to act.";
                return;
            }
        }

        try
        {
            currentSession = await backendClient.PassAsync(currentSessionId, PlayerSeatIndex);
            playerHasPassedThisHand = true;
            UpdateUiFromSession("Player (seat 0) passed.");

            await AutoBidForBotsAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            if (sessionInfoText != null)
                sessionInfoText.text = "Error passing. See console.";
        }
    }

    // ---------- Optional manual kitty reveal (AI also uses this) ----------

    private async void OnRevealKittyClicked()
    {
        if (!EnsureSession()) return;

        try
        {
            currentSession = await backendClient.RevealKittyAsync(currentSessionId);
            UpdateUiFromSession("Kitty revealed (manual).");
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            if (sessionInfoText != null)
                sessionInfoText.text = "Error revealing kitty.";
        }
    }

    // ---------- Player play + AI play ----------

    private async void OnPlayPlayerCardClicked()
    {
        if (!EnsureSession()) return;

        if (currentSession.phase != "Playing")
        {
            if (sessionInfoText != null)
                sessionInfoText.text = $"Phase is {currentSession.phase}, not Playing.";
            return;
        }

        if (currentSession.currentSeatToPlay == null)
        {
            if (sessionInfoText != null)
                sessionInfoText.text = "No current seat to play.";
            return;
        }

        // Let AI handle their turns first if it's not your turn.
        if (currentSession.currentSeatToPlay != PlayerSeatIndex)
        {
            await AutoPlayAiUntilPlayerTurnOrEnd();
            if (!EnsureSession() || currentSession.phase != "Playing")
                return;

            if (currentSession.currentSeatToPlay != PlayerSeatIndex)
            {
                if (sessionInfoText != null)
                    sessionInfoText.text = $"Still not your turn (seat {currentSession.currentSeatToPlay}).";
                return;
            }
        }

        int cardIndex = ParseCardIndex();

        try
        {
            currentSession = await backendClient.PlayCardAsync(currentSessionId, PlayerSeatIndex, cardIndex);
            UpdateUiFromSession($"Player (seat 0) played card index {cardIndex}.");

            await AutoPlayAiUntilPlayerTurnOrEnd();
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            if (sessionInfoText != null)
                sessionInfoText.text = "Error playing card. See console.";
        }
    }

    private async System.Threading.Tasks.Task AutoPlayAiUntilPlayerTurnOrEnd()
    {
        if (!EnsureSession()) return;

        int safety = 0;
        while (currentSession != null &&
               currentSession.phase == "Playing" &&
               currentSession.currentSeatToPlay != null &&
               currentSession.currentSeatToPlay != PlayerSeatIndex &&
               safety < 200)
        {
            safety++;

            int seat = currentSession.currentSeatToPlay.Value;

            var seatHand = currentSession.hands.FirstOrDefault(h => h.seatIndex == seat);
            if (seatHand == null || seatHand.cards == null || seatHand.cards.Count == 0)
            {
                UpdateUiFromSession($"AI seat {seat} has no cards left.");
                break;
            }

            // Very simple AI play: just play the first card.
            currentSession = await backendClient.PlayCardAsync(currentSessionId, seat, 0);
            UpdateUiFromSession($"AI seat {seat} played first card.");
        }

        if (currentSession != null && currentSession.phase == "Completed")
        {
            ApplyHandToMatchScores();
            UpdateUiFromSession("Hand completed.");
        }
    }

    // ---------- AI bidding helper ----------

    private struct BotBid
    {
        public int tricks;
        public string trump;
    }

    private BotBid? ChooseBotBid(int seatIndex)
    {
        var seatHand = currentSession.hands.FirstOrDefault(h => h.seatIndex == seatIndex);
        if (seatHand == null || seatHand.cards == null || seatHand.cards.Count == 0)
            return null;

        // Evaluate each suit
        var scores = new Dictionary<string, float>();
        foreach (var s in trumpOrder)
        {
            scores[s] = 0f;
        }

        foreach (var card in seatHand.cards)
        {
            if (card.isJoker)
            {
                foreach (var s in trumpOrder)
                    scores[s] += 2.0f;
                continue;
            }

            if (string.IsNullOrEmpty(card.suit) || string.IsNullOrEmpty(card.rank))
                continue;

            float value = card.rank switch
            {
                "Ace" => 3.0f,
                "King" => 2.5f,
                "Queen" => 2.0f,
                "Jack" => 1.5f,
                "Ten" => 1.0f,
                _ => 0.5f
            };

            if (scores.ContainsKey(card.suit))
                scores[card.suit] += value;
        }

        // Best suit by score
        string bestSuit = trumpOrder[0];
        float bestScore = scores[bestSuit];
        foreach (var kvp in scores)
        {
            if (kvp.Value > bestScore)
            {
                bestSuit = kvp.Key;
                bestScore = kvp.Value;
            }
        }

        // Compute current high bid if any
        int highStrength = -1;
        int highTricks = 0;

        if (currentSession.bidding.currentHighBid != null)
        {
            highStrength = currentSession.bidding.currentHighBid.strength;
            highTricks = currentSession.bidding.currentHighBid.tricks;
        }

        // If there is NO high bid yet, open with 6 in best suit.
        if (highStrength < 0)
        {
            return new BotBid
            {
                tricks = 6,
                trump = bestSuit
            };
        }

        // From here on, we're trying to overcall an existing bid: use a threshold.
        if (bestScore < 8.0f)
            return null;

        int maxTricks = bestScore > 12.0f ? 8 : 7;

        int bestSuitIndex = Array.IndexOf(trumpOrder, bestSuit);
        if (bestSuitIndex < 0)
            bestSuitIndex = 0;

        // Try to find minimal contract that beats current high bid
        for (int tricks = highTricks; tricks <= maxTricks; tricks++)
        {
            for (int suitIndex = bestSuitIndex; suitIndex < trumpOrder.Length; suitIndex++)
            {
                string candidateTrump = trumpOrder[suitIndex];
                int candidateStrength = (tricks - 6) * 10 + (suitIndex + 1);

                if (candidateStrength > highStrength)
                {
                    return new BotBid
                    {
                        tricks = tricks,
                        trump = candidateTrump
                    };
                }
            }
        }

        // Can't beat current high bid with our simple rules -> pass
        return null;
    }

    private async System.Threading.Tasks.Task AutoBidForBotsAsync()
    {
        if (!EnsureSession()) return;

        // If bidding is already over, either go to Kitty or ignore
        if (currentSession.phase != "Bidding" || currentSession.bidding == null)
        {
            if (currentSession.phase == "Kitty")
            {
                currentSession = await backendClient.RevealKittyAsync(currentSessionId);
                UpdateUiFromSession("Kitty revealed automatically after bidding.");
            }
            return;
        }

        int safety = 0;

        while (currentSession.phase == "Bidding" &&
               currentSession.bidding != null &&
               !currentSession.bidding.isCompleted &&
               currentSession.bidding.currentSeatIndex != PlayerSeatIndex &&
               safety < 50)
        {
            safety++;

            int seat = currentSession.bidding.currentSeatIndex;
            var botBid = ChooseBotBid(seat);

            try
            {
                if (botBid == null)
                {
                    currentSession = await backendClient.PassAsync(currentSessionId, seat);
                    UpdateUiFromSession($"AI seat {seat} passed.");
                }
                else
                {
                    currentSession = await backendClient.PlaceBidAsync(
                        currentSessionId,
                        seat,
                        botBid.Value.tricks,
                        botBid.Value.trump);
                    UpdateUiFromSession($"AI seat {seat} bid {botBid.Value.tricks} {botBid.Value.trump}.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                // Fallback: pass
                currentSession = await backendClient.PassAsync(currentSessionId, seat);
                UpdateUiFromSession($"AI seat {seat} passed (fallback).");
            }

            if (currentSession.phase != "Bidding" || currentSession.bidding == null)
                break;
        }

        // Edge case: everyone passed
        if (currentSession.phase == "Bidding" &&
            currentSession.bidding != null &&
            currentSession.bidding.currentHighBid == null &&
            currentSession.bidding.actions != null &&
            currentSession.bidding.actions.Count >= currentSession.playerCount)
        {
            UpdateUiFromSession("Everyone passed. Starting a new hand.");
            OnHostSessionClicked();
            return;
        }

        // If bidding has ended properly, we should be in Kitty phase now
        if (currentSession.phase == "Kitty")
        {
            currentSession = await backendClient.RevealKittyAsync(currentSessionId);
            UpdateUiFromSession("Kitty revealed automatically after AI bidding.");
        }
    }

    // ---------- Hand sorting ----------

    private void OnSortHandClicked()
    {
        if (!EnsureSession()) return;

        var seat0 = currentSession.hands?.FirstOrDefault(h => h.seatIndex == PlayerSeatIndex);
        if (seat0?.cards == null || seat0.cards.Count == 0)
        {
            if (sessionInfoText != null)
                sessionInfoText.text = "No cards in hand to sort.";
            return;
        }

        seat0.cards.Sort(CompareCardsForSort);
        UpdateUiFromSession("Hand sorted by suit, Ace high, Jacks highlighted.");
    }

    private int CompareCardsForSort(CardDto a, CardDto b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        // Jokers first
        if (a.isJoker && !b.isJoker) return -1;
        if (!a.isJoker && b.isJoker) return 1;
        if (a.isJoker && b.isJoker) return 0;

        // Suit order
        int suitA = suitSortOrder.TryGetValue(a.suit ?? "", out var sa) ? sa : 999;
        int suitB = suitSortOrder.TryGetValue(b.suit ?? "", out var sb) ? sb : 999;
        int suitCompare = suitA.CompareTo(suitB);
        if (suitCompare != 0) return suitCompare;

        // Rank: Ace high → low (so we sort by rank index descending)
        int rankA = rankSortOrder.TryGetValue(a.rank ?? "", out var ra) ? ra : -1;
        int rankB = rankSortOrder.TryGetValue(b.rank ?? "", out var rb) ? rb : -1;

        // Higher rank index first
        return rankB.CompareTo(rankA);
    }

    // ---------- Helpers ----------

    private bool EnsureSession()
    {
        if (string.IsNullOrEmpty(currentSessionId) || currentSession == null)
        {
            if (sessionInfoText != null)
                sessionInfoText.text = "Host a session/hand first.";
            return false;
        }
        return true;
    }

    private int ParseCardIndex()
    {
        if (cardIndexInput == null)
        {
            Debug.LogWarning("CardIndexInput is not assigned; defaulting to 0.");
            return 0;
        }

        if (int.TryParse(cardIndexInput.text, out var idx))
        {
            if (idx < 0) idx = 0;
            return idx;
        }

        cardIndexInput.text = "0";
        if (sessionInfoText != null)
            sessionInfoText.text = "Card index input invalid; using 0. Enter a number like 0,1,2,...";
        return 0;
    }

    private int GetSelectedTricks()
    {
        if (tricksDropdown == null || tricksDropdown.options.Count == 0)
            return 6;

        string label = tricksDropdown.options[tricksDropdown.value].text;
        if (int.TryParse(label, out int tricks))
            return Mathf.Clamp(tricks, 6, 10);

        return 6;
    }

    private string GetSelectedTrump()
    {
        if (trumpDropdown == null || trumpDropdown.options.Count == 0)
            return "Hearts";

        return trumpDropdown.options[trumpDropdown.value].text;
    }

    private void ApplyHandToMatchScores()
    {
        if (currentHandScored || currentSession == null)
            return;

        matchScoreA += currentSession.teamScoreA;
        matchScoreB += currentSession.teamScoreB;
        currentHandScored = true;
    }

    private void UpdateUiFromSession(string lastAction)
    {
        if (currentSession == null)
            return;

        // Hand for seat 0
        if (handSeat0Text != null)
        {
            var seat0 = currentSession.hands?.FirstOrDefault(h => h.seatIndex == PlayerSeatIndex);
            if (seat0?.cards != null)
            {
                var lines = seat0.cards
                    .Select(CardToString)
                    .Select((c, i) => $"{i}: {c}");
                handSeat0Text.text = "Seat 0 hand:\n(index: card)\n" +
                                     string.Join("\n", lines);
            }
            else
            {
                handSeat0Text.text = "Seat 0 hand: (none)";
            }
        }

        // Session summary
        if (sessionInfoText != null)
        {
            var b = currentSession.bidding;

            string highBidText = b?.currentHighBid != null
                ? $"Seat {b.currentHighBid.seatIndex} {b.currentHighBid.tricks} {b.currentHighBid.trump}"
                : "(none)";

            string contractSummary = currentSession.contractTricks.HasValue
                ? $"Seat {currentSession.contractSeatIndex} {currentSession.contractTricks} {currentSession.contractTrump}"
                : "(none)";

            string status =
                $"Session {currentSession.id}\n" +
                $"Phase: {currentSession.phase}\n" +
                $"Dealer: {b?.dealerSeatIndex}\n" +
                $"Bidding current seat: {b?.currentSeatIndex}\n" +
                $"High bid: {highBidText}\n" +
                $"Bidding complete: {b?.isCompleted}\n\n" +
                $"Current seat to play: {currentSession.currentSeatToPlay?.ToString() ?? "-"}\n" +
                $"Contract: {contractSummary}\n" +
                $"Contract made: {currentSession.contractMade?.ToString() ?? "-"}\n" +
                $"Tricks (contract / defence): {currentSession.tricksWonByContractTeam} / {currentSession.tricksWonByDefence}\n" +
                $"Hand Score A / B: {currentSession.teamScoreA} / {currentSession.teamScoreB}\n" +
                $"Match Score A / B: {matchScoreA} / {matchScoreB}\n\n" +
                $"Last action: {lastAction}";

            sessionInfoText.text = status;
        }

        string phase = currentSession.phase ?? string.Empty;

        if (revealKittyButton != null)
            revealKittyButton.interactable = phase == "Kitty";

        if (playCardButton != null)
            playCardButton.interactable = phase == "Playing";

        if (submitBidButton != null)
            submitBidButton.interactable = phase == "Bidding" && !playerHasPassedThisHand;

        if (passButton != null)
            passButton.interactable = phase == "Bidding" && !playerHasPassedThisHand;

        if (nextHandButton != null)
            nextHandButton.interactable = phase == "Completed";
    }

    private string CardToString(CardDto c)
    {
        if (c == null) return "(null)";

        if (c.isJoker)
            return "Joker";

        string label = $"{c.rank} of {c.suit}";

        // Highlight Jacks
        if (!string.IsNullOrEmpty(c.rank) &&
            c.rank.Equals("Jack", StringComparison.OrdinalIgnoreCase))
        {
            label = "[J] " + label;
        }

        return label;
    }
}
