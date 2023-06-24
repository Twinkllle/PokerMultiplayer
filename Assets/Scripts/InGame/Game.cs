using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class Game : NetworkBehaviour
{
    public static Game Instance { get; private set; }

    public event Action<GameStage> GameStageBeganEvent;
    public event Action<GameStage> GameStageOverEvent;
    public event Action<WinnerInfo[]> EndDealEvent;

    public string CodedBoardCardsString => _codedBoardCardsString.Value.ToString();
    private readonly NetworkVariable<FixedString64Bytes> _codedBoardCardsString = new();

    public List<CardObject> BoardCards => _board.Cards.ToList();

    public bool IsPlaying => _isPlaying.Value;
    private readonly NetworkVariable<bool> _isPlaying = new();

    private static Betting Betting => Betting.Instance;
    private static PlayerSeats PlayerSeats => PlayerSeats.Instance;
    private static Pot Pot => Pot.Instance;
    
    [SerializeField] private BoardButton _boardButton;
    [ReadOnly] [SerializeField] private Board _board;
    private CardDeck _cardDeck;

    private IEnumerator _stageCoroutine;
    private IEnumerator _startDealWhenСonditionTrueCoroutine;
    private IEnumerator _startDealAfterRoundsInterval;

    public GameStage CurrentGameStage => _currentGameStage.Value;
    private readonly NetworkVariable<GameStage> _currentGameStage = new();
    
    private bool ConditionToStartDeal => _isPlaying.Value == false && 
                                         PlayerSeats.PlayersAmount >= 2 && 
                                         PlayerSeats.Players.Where(x => x != null).All(x => x.BetAmount == 0);

    [SerializeField] private float _roundsInterval;
    [SerializeField] private float _showdownEndTime;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void OnEnable()
    {
        PlayerSeats.PlayerSitEvent += OnPlayerSit;
        PlayerSeats.PlayerLeaveEvent += OnPlayerLeave;
    }

    private void OnDisable()
    {
        PlayerSeats.PlayerSitEvent -= OnPlayerSit;
        PlayerSeats.PlayerLeaveEvent -= OnPlayerLeave;
    }

    private IEnumerator StartPreflop()
    {
        if (IsServer == false)
        {
            Log.WriteToFile("Error: Preflop stage wanted to performed on client.");
            yield break;
        }

        int[] turnSequence = _boardButton.GetTurnSequence();
        foreach (int index in turnSequence)
        {
            Player player = PlayerSeats.Players[index];
            SetPlayersPocketCardsClientRpc(player.OwnerClientId, _cardDeck.PullCard(), _cardDeck.PullCard());
        }
        
        Player player1 = PlayerSeats.Players[turnSequence[0]];
        Player player2 = PlayerSeats.Players[turnSequence[1]];
        yield return Betting.AutoBetBlinds(player1, player2);

        int[] preflopTurnSequence = _boardButton.GetPreflopTurnSequence();

        yield return Bet(preflopTurnSequence);
        
        S_EndStage();

        yield return new WaitForSeconds(_roundsInterval);

        S_StartNextStage();
    }

    // Stage like Flop, Turn and River.
    private IEnumerator StartMidGameStage()
    {
        if (IsServer == false)
        {
            Log.WriteToFile("Error: MidGame stage wanted to performed on client.");
            yield break;
        }
        
        if (Betting.IsAllIn == false)
        {
            int[] turnSequence = _boardButton.GetTurnSequence();
            yield return Bet(turnSequence);
        }

        S_EndStage();

        yield return new WaitForSeconds(_roundsInterval);
    }

    private IEnumerator StartShowdown()
    {
        if (IsServer == false)
        {
            Log.WriteToFile("Error: Showdown stage wanted to performed on client.");
            yield break;
        }
        
        int[] turnSequence = _boardButton.GetShowdownTurnSequence();
        
        List<Player> winners = new();
        Hand winnerHand = new();
        for (var i = 0; i < turnSequence.Length; i++)
        {
            Player player = PlayerSeats.Players[turnSequence[i]];
            List<CardObject> completeCards = _board.Cards.ToList();
            completeCards.Add(player.PocketCard1); completeCards.Add(player.PocketCard2);

            Hand bestHand = CombinationСalculator.GetBestHand(new Hand(completeCards));

            if (i == 0 || bestHand > winnerHand)
            {
                winners.Clear();
                winners.Add(player);
                winnerHand = bestHand;
            }
            else if (bestHand == winnerHand)
            {
                winners.Add(player);
            }
        }
        
        if (winners.Count == 0)
        {
            throw new NullReferenceException();
        }

        S_EndStage();
        
        yield return new WaitForSeconds(_showdownEndTime);

        List<WinnerInfo> winnerInfo = new();
        foreach (Player winner in winners)
        {
            winnerInfo.Add(new WinnerInfo(winner.OwnerClientId, Pot.GetWinValue(winner, winners), winnerHand.ToString()));
        }

        S_EndDeal(winnerInfo.ToArray());
    }

    private IEnumerator Bet(int[] turnSequence)
    {
        if (IsServer == false)
        {
            Log.WriteToFile("Error: Betting wanted to performed on client.");
            yield break;
        }
        
        for (var i = 0;; i++)
        {
            foreach (int index in turnSequence)
            {
                Player player = PlayerSeats.Players[index];

                if (player == null)
                {
                    continue;
                }

                yield return Betting.Bet(player);
            
                List<Player> notFoldPlayers = PlayerSeats.Players.Where(x => x != null && x.BetAction != BetAction.Fold).ToList();
                if (notFoldPlayers.Count == 1)
                {
                    ulong winnerId = notFoldPlayers[0].OwnerClientId;
                    WinnerInfo[] winnerInfo = {new(winnerId, Pot.GetWinValue(notFoldPlayers[0], new []{notFoldPlayers[0]}))};
                    S_EndDeal(winnerInfo);
                    yield break;
                }

                if (i == 0 || IsBetsEquals() == false)
                {
                    continue;
                }

                yield break;
            }

            if (i != 0 || IsBetsEquals() == false)
            {
                continue;
            }

            yield break;
        }
    }
        
    private void OnPlayerSit(Player player, int seatNumber)
    {
        if (IsServer == false)
        {
            return;
        }
        
        if (_startDealAfterRoundsInterval != null || IsPlaying == true)   
        {
            return;
        }
        
        _startDealAfterRoundsInterval = StartDealAfterRoundsInterval();
        StartCoroutine(_startDealAfterRoundsInterval);
    }

    private void OnPlayerLeave(Player player, int seatNumber)
    {
        if (IsServer == false)
        {
            return;
        }
        
        if (_isPlaying.Value == false)
        {
            return; 
        }

        if (PlayerSeats.Players.Count(x => x != null && x.BetAction != BetAction.Fold) != 1)
        {
            return;
        }
        
        Player winner = PlayerSeats.Players.FirstOrDefault(x => x != null);
        ulong winnerId = winner!.OwnerClientId; 
        WinnerInfo[] winnerInfo = {new(winnerId, Pot.GetWinValue(winner, new []{winner}))};
        S_EndDeal(winnerInfo);
    }

    private IEnumerator StartDealAfterRoundsInterval()
    {
        yield return new WaitForSeconds(_roundsInterval);

        PlayerSeats.SitEveryoneWaiting();
        PlayerSeats.KickPlayersWithZeroStack();
        
        if (IsServer == false || _startDealWhenСonditionTrueCoroutine != null)
        {
            yield break;
        }
        
        _startDealWhenСonditionTrueCoroutine = StartDealWhenСonditionTrue();
        yield return StartCoroutine(_startDealWhenСonditionTrueCoroutine);

        _startDealAfterRoundsInterval = null;
    }
    
    private IEnumerator StartDealWhenСonditionTrue()
    {
        yield return new WaitUntil(() => ConditionToStartDeal == true);
        yield return new WaitForSeconds(0.05f);

        S_StartDeal();

        _startDealWhenСonditionTrueCoroutine = null;
    }

    private void GetStageCoroutine(GameStage gameStage)
    {
        switch (gameStage)
        {
            case GameStage.Preflop:
                _stageCoroutine = StartPreflop();
                break;
            
            case GameStage.Flop:
            case GameStage.Turn:
            case GameStage.River:
                _stageCoroutine = StartMidGameStage();
                break;
            
            case GameStage.Showdown:
                _stageCoroutine = StartShowdown();
                break;
            
            default:
                throw new ArgumentOutOfRangeException(nameof(_currentGameStage), _currentGameStage.Value, null);
        }
    }
    
    private static bool IsBetsEquals()
    {
        return PlayerSeats.Players.Where(x => x != null && x.BetAction != BetAction.Fold).Select(x => x.BetAmount).Distinct().Skip(1).Any() == false;
    }
    
    #region Server

    private void S_StartDeal()
    {
        if (IsServer == false)
        {
            return;
        }
        
        Log.WriteToFile("Starting Deal.");
        
        _cardDeck = new CardDeck();

        StartDealClientRpc(CardObjectConverter.GetCodedCards(_cardDeck.Cards));

        _board = new Board(_cardDeck.PullCards(5).ToList());

        _boardButton.Move();
        
        SetCodedBoardCardsValueServerRpc(CardObjectConverter.GetCodedCardsString(_board.Cards));
        SetIsPlayingValueServerRpc(true);

        S_StartNextStage();
    }
    
    private void S_EndDeal(WinnerInfo[] winnerInfo)
    {
        if (IsServer == false)
        {
            return;
        }
        
        SetCurrentGameStageValueServerRpc(GameStage.Empty);
        SetIsPlayingValueServerRpc(false);
        SetCodedBoardCardsValueServerRpc(string.Empty);
        
        Log.WriteToFile($"End deal. Winner id(`s): '{string.Join(", ", winnerInfo.Select(x => x.WinnerId))}'. Winner hand: {winnerInfo[0].Combination}");

        if (_startDealAfterRoundsInterval != null)
        {
            StopCoroutine(_startDealAfterRoundsInterval);
        }

        _startDealAfterRoundsInterval = StartDealAfterRoundsInterval();
        StartCoroutine(_startDealAfterRoundsInterval);

        EndDealClientRpc(winnerInfo);
    }
    
    private void S_StartNextStage()
    {
        if (IsServer == false)
        {
            return;
        }

        GameStage stage = _currentGameStage.Value + 1;
        
        SetCurrentGameStageValueServerRpc(stage);
        GetStageCoroutine(stage);
        StartCoroutine(_stageCoroutine);
        
        StartNextStageClientRpc(stage);
        
        Log.WriteToFile($"Starting {stage} stage.");
    }

    private void S_EndStage()
    {
        if (IsServer == false)
        {
            return;
        }

        EndStageClientRpc();
    }
    
    #endregion
    
    #region RPC

    [ServerRpc]
    private void SetIsPlayingValueServerRpc(bool value)
    {
        _isPlaying.Value = value;
    }

    [ServerRpc]
    private void SetCurrentGameStageValueServerRpc(GameStage value)
    {
        _currentGameStage.Value = value;
    }

    [ServerRpc]
    private void SetCodedBoardCardsValueServerRpc(string value)
    {
        _codedBoardCardsString.Value = value;
    }

    [ClientRpc]
    private void SetPlayersPocketCardsClientRpc(ulong playerId, CardObject card1, CardObject card2)
    {
        Player player = PlayerSeats.Players.FirstOrDefault(x => x != null && x.OwnerClientId == playerId);
        if (player == null)
        {
            return;
        }
        
        player.SetPocketCards(card1, card2);
    }

    [ClientRpc]
    private void StartDealClientRpc(int[] cardDeck)
    {
        _cardDeck = new CardDeck(cardDeck);
        _board = new Board(_cardDeck.PullCards(5).ToList());
    }

    [ClientRpc]
    private void EndDealClientRpc(WinnerInfo[] winnerInfo)
    {
        EndDealEvent?.Invoke(winnerInfo);
    }

    [ClientRpc]
    private void StartNextStageClientRpc(GameStage stage)
    {
        GameStageBeganEvent?.Invoke(stage);
    }

    [ClientRpc]
    private void EndStageClientRpc()
    {
        GameStageOverEvent?.Invoke(GameStage.Showdown);
    }
    
    #endregion
}