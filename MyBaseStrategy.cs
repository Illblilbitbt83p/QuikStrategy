using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows;
using System.Windows.Media;
using StockSharp.Algo;
using StockSharp.Algo.Strategies;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using System.Net;
using System.Net.Mail;
using System.IO;
using StockSharp.Messages;
using StockSharp.Quik;
// Для работы с коллекциямии
// Для LINQ запросов
// Для отправки почты
// Для работы с файлами

namespace PrismaBoy
{
    // Объявляем делегат обработчика события измения Gross стратегии
    public delegate void GrossChangedEventHandler(object sender, GrossChangedEventArgs e);

    // Объявляем класс объекта (и его конструктор) для передачи параметров при событии изменения Gross стратегии
    public class GrossChangedEventArgs : EventArgs
    {
        public decimal NewGross { get; private set; }

        public GrossChangedEventArgs(decimal newValue)
        {
            NewGross = newValue;
        }
    }

    // Объявляем делегат обработчика события измения Active Trades стратегии
    public delegate void ActiveTradesChangedEventHandler(object sender, ActiveTradesChangedEventArgs e);

    // Объявляем класс объекта (и его конструктор) для передачи параметров при событии изменения Active Trades
    public class ActiveTradesChangedEventArgs : EventArgs
    {
    }

    // --------------------------- !!! БАЗОВАЯ СТРАТЕГИЯ !!! ---------------------------

    internal abstract class MyBaseStrategy : Strategy, INotifyPropertyChanged
    {
        /// <summary>
        /// Коллекция инструментов стратегии
        /// </summary>
        protected List<Security> SecurityList;

        /// <summary>
        /// Коллекция распределения объемов торгуемых инструментов стратегии
        /// </summary>
        protected Dictionary<string, decimal> SecurityVolumeDictionary;

        /// <summary>
        /// Таймфрейм стратегии
        /// </summary>
        protected readonly TimeSpan TimeFrame;

        /// <summary>
        /// Является ли контур на котором запущена стратегия рабочим
        /// </summary>
        public bool IsWorkContour;

        /// <summary>
        /// Дневные лимиты стратегии
        /// </summary>
        public decimal DailyLimitLoss { get; set; }

        /// <summary>
        /// Коллекция строк с отчетом по сделкам на отправку
        /// </summary>
        public List<string> InfoToEmail { get; set; }

        /// <summary>
        /// Время последней отправки E-mail
        /// </summary>
        protected DateTime LastTimeEmail;

        /// <summary>
        /// Является ли остановка робота остановкой по расписанию
        /// </summary>
        private bool _isStopRobotByShedule;

        /// <summary>
        /// Закрывать все позиции при остановке. По-умолчанию включено.
        /// </summary>
        public bool CloseAllPositionsOnStop;

        /// <summary>
        /// Является ли стратегия внутридневной
        /// </summary>
        public bool IsIntraDay;

        #region Параметры стопов и профитов

        /// <summary>
        /// Стоплосс стратегии, %
        /// </summary>
        protected readonly decimal StopLossPercent;

        /// <summary>
        /// Тип стопа
        /// </summary>
        protected StopTypes StopType;

        /// <summary>
        /// Тейкпрофит стратегии, %
        /// </summary>
        protected readonly decimal TakeProfitPercent;

        /// <summary>
        /// Выход по времени, свечек
        /// </summary>
        protected int BarsToClose;

        #endregion

        #region Коллекции и словари с информацией по текущим сделкам, позициям итд...

        /// <summary>
        /// Коллекция активных ("не закрытых") сделок
        /// </summary>
        public List<ActiveTrade> ActiveTrades;

        /// <summary>
        /// Словарь позиций по инструментам стратегии
        /// </summary>
        protected Dictionary<string, decimal> PositionsDictionary;

        /// <summary>
        /// Словарь последних сделок по инструментам стратегии
        /// </summary>
        protected readonly Dictionary<string, decimal> LastTradesDictionary;

        #endregion

        #region Временные параметры

        /// <summary>
        /// Время запуска стратегии
        /// </summary>
        public TimeOfDay TimeToStartRobot;

        /// <summary>
        /// Время отправить почту в промышленный клиринг
        /// </summary>
        private DateTime _timeToSendMailInDayClearing;

        /// <summary>
        /// Время отправить почту в вечерний клиринг
        /// </summary>
        private DateTime _timeToSendMailInEveningClearing;

        /// <summary>
        /// Время остановки стратегии
        /// </summary>
        public DateTime TimeToStopRobot;

        #endregion

        #region События

        /// <summary>
        /// Делегат обработчика изменения свойств стратегии
        /// </summary>
        public new event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Метод обработки события изменения свойств стратегии
        /// </summary>
        private void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Делегат обработчика изменения Gross стратегии
        /// </summary>
        public event GrossChangedEventHandler GrossChanged;

        /// <summary>
        /// Метод обработки события изменения Gross стратегии
        /// </summary>
        public virtual void OnGrossChanged(GrossChangedEventArgs e)
        {
            var handler = GrossChanged;
            if (handler != null)
                handler(this, new GrossChangedEventArgs(e.NewGross));
        }

        /// <summary>
        /// Поле Gross стратегии
        /// </summary>
        private decimal _gross;

        /// <summary>
        /// Свойство Gross стратегии
        /// </summary> 
        public decimal Gross
        {
            get
            {
                return _gross;
            }
            set
            {
                var oldValue = _gross;
                _gross = value;
                if (_gross > 0)
                    GrossBrush = Brushes.DarkGreen;
                if (_gross < 0)
                    GrossBrush = Brushes.Red;
                if (_gross == 0)
                    GrossBrush = Brushes.Yellow;
                if (oldValue != _gross)
                    OnGrossChanged(new GrossChangedEventArgs(_gross));
                OnPropertyChanged("Gross");
            }
        }

        /// <summary>
        /// Поле цвет-Gross стратегии
        /// </summary>
        private SolidColorBrush _grossBrush;

        /// <summary>
        /// Свойство цвет-Gross стратегии
        /// </summary>
        public SolidColorBrush GrossBrush
        {
            get
            {
                return _grossBrush;
            }
            set
            {
                _grossBrush = value;
                OnPropertyChanged("GrossBrush");
            }
        }

        #endregion

        /// <summary>
        /// Делегат обработчика изменения свойств стратегии
        /// </summary>
        public event ActiveTradesChangedEventHandler ActiveTradesChanged;

        /// <summary>
        /// Метод обработки события изменения ActiveTrades
        /// </summary>
        protected void OnActiveTradesChanged(ActiveTradesChangedEventArgs e)
        {
            var handler = ActiveTradesChanged;
            if (handler != null)
                handler(this, e);
        }

        /// <summary>
        /// Конструктор
        /// </summary>
        protected MyBaseStrategy(List<Security> securityList, Dictionary<string, decimal> securityVolumeDictionary, TimeSpan timeFrame, decimal stopLossPercent, decimal takeProfitPercent)
        {
            // В соответствии с параметрами конструктора
            SecurityList = securityList;
            SecurityVolumeDictionary = securityVolumeDictionary;
            TimeFrame = timeFrame;
            StopLossPercent = stopLossPercent;
            TakeProfitPercent = takeProfitPercent;

            // Параметры по умолчанию
            DailyLimitLoss = -1;                            // Без ограничения дневных потерь
            BarsToClose = -1;                               // Без выхода по времени через n баров
            CloseAllPositionsOnStop = true;                 // Закрытие всех позиций при остановке стратегии
            TimeToStartRobot = new TimeOfDay(9, 50);        // Время запуска стратегии в 9:50
            StopType = StopTypes.MarketLimitOffer;          // Тип стопа - по лучшей предлагаемой цене для нас

            // Объявляем пустые переменные
            InfoToEmail = new List<string>();
            LastTimeEmail = DateTime.Now;

            ActiveTrades = new List<ActiveTrade>();

            PositionsDictionary = new Dictionary<string, decimal>();
            LastTradesDictionary = new Dictionary<string, decimal>();

            foreach (var security in SecurityList)
            {
                PositionsDictionary.Add(security.Code, 0);
                LastTradesDictionary.Add(security.Code, 0);
            }

            Gross = 0;
            GrossBrush = Brushes.Black;
        }

        /// <summary>
        /// Событие старта стратегии
        /// </summary>
        protected override void OnStarted()
        {
            #region Подписываемся на заданное время для отправки писем с отчетами

            switch (DateTime.Today.DayOfWeek)
            {
                case DayOfWeek.Saturday:
                    _timeToSendMailInDayClearing =
                        DateTime.Today.AddHours(14).AddMinutes(01).AddDays(2);
                    _timeToSendMailInEveningClearing =
                        DateTime.Today.AddHours(18).AddMinutes(51).AddDays(2);
                    break;

                case DayOfWeek.Sunday:
                    _timeToSendMailInDayClearing =
                        DateTime.Today.AddHours(14).AddMinutes(01).AddDays(1);
                    _timeToSendMailInEveningClearing =
                        DateTime.Today.AddHours(18).AddMinutes(51).AddDays(1);
                    break;

                default:
                    _timeToSendMailInDayClearing =
                        DateTime.Today.AddHours(14).AddMinutes(01);
                    _timeToSendMailInEveningClearing =
                        DateTime.Today.AddHours(18).AddMinutes(51);
                    break;
            }

            Security
                .WhenTimeCome(_timeToSendMailInDayClearing)
                .Once()
                .Do(() => PrepareAndSendMail(false))
                .Apply(this);

            Security
                .WhenTimeCome(_timeToSendMailInEveningClearing)
                .Once()
                .Do(() => PrepareAndSendMail(true))
                .Apply(this);

            this.AddInfoLog("Время отправки писем с отчетами:\nУтро - " + _timeToSendMailInDayClearing + "\nВечер - " +
                            _timeToSendMailInEveningClearing);

            #endregion

            #region Подписываемся на время завершения работы робота (если он внутридневной)

            if (IsIntraDay)
            {
                Security
                .WhenTimeCome(TimeToStopRobot.Subtract(new TimeSpan(0, 0, 0, 5)))
                .Once()
                .Do(() =>                   // Отправляем отчет и останавливаем стратегию
                {
                    this.AddInfoLog("Стратегия " + Name + " останавливается по расписанию");

                    // Остановка по расписанию
                    _isStopRobotByShedule = true;

                    StopRobot();
                })
                    .Apply(this);
            }

            #endregion

            #region Обрабатываем подгруженные активные сделки

            // Если при старте стратегии подгрузились заявки, то просматриваем тейкпрофит заявки
            if (ActiveTrades.Any())
            {
                var sumstr = ActiveTrades.Aggregate("", (current, trade) => current + (trade.Security + "," + trade.Direction + "," + trade.Volume + "\t\n"));

                this.AddInfoLog("АКТИВНЫЕ ПОЗИЦИИ:\n" + sumstr);

                foreach (var trade in ActiveTrades)
                {
                    // Если нет тейкпрофит заявок, то ничего не делаем
                    if (Connector.Orders.All(order => order.Id != trade.ProfitOrderId))
                    {
                        this.AddInfoLog("Не нашли тейкпрофит заявку N {0}", trade.ProfitOrderId);
                        continue;
                    }

                    // Если есть, то смотрим исполнена ли тейкпрофит заявка
                    var currentActiveTrade = trade;

                    var profitOrder =
                        Connector.Orders.First(order => order.Id == currentActiveTrade.ProfitOrderId);

                    // Если исполнена, то обновляем список активных трейдов(удаляем)
                    if (profitOrder.IsMatched())
                    {
                        this.AddInfoLog("ВЫХОД по ПРОФИТУ - {0}", profitOrder.Security);

                        ActiveTrades = ActiveTrades.Where(activeTrade => activeTrade != currentActiveTrade).ToList();

                        // Вызываем событие прихода изменения ActiveTrades
                        OnActiveTradesChanged(new ActiveTradesChangedEventArgs());
                    }
                    // Если не исполена, то для того, чтобы подписаться на исполнений тейкпрофит заявки, перевыставяем тейкпрофит заявку
                    else
                    {
                        Connector.CancelOrder(profitOrder);
                        PlaceProfitOrder(currentActiveTrade);
                    }
                }
            }
            #endregion

            // Обнуляем счетчик Gross стратегии
            Gross = 0;

            // Сбрасываем(обновляем) булеву переменную остановки по расписанию
            _isStopRobotByShedule = false;

            // Подписываемся на событие появления новых сделок на рынке (для реализации защиты по стопу и сбора информации о сделках)
            Connector.NewTrades -= TraderNewTrades;
            Connector.NewTrades += TraderNewTrades;

            // Если стратегия таймфреймовая, то подписываемся на события прихода свечи
            if (TimeFrame != TimeSpan.Zero)
            {
                MainWindow.Instance.TimeFrameCome -= TimeFrameCome;
                MainWindow.Instance.TimeFrameCome += TimeFrameCome;
            }

            // Если стратегия с возможным переносом позиций на следующий день, то подписываемся на событие изменения активных сделок для их сохранения
            if (IsIntraDay == false)
            {
                ActiveTradesChanged -= ActiveTradesChanging;
                ActiveTradesChanged += ActiveTradesChanging;
            }

            base.OnStarted();
        }

        /// <summary>
        /// Метод-обработчик изменения ActiveTrades
        /// </summary>
        private void ActiveTradesChanging(object sender, ActiveTradesChangedEventArgs e)
        {
            SaveActiveTradesAndPositions();
        }

        /// <summary>
        /// Метод-обработчик прихода новой свечки
        /// </summary>
        protected virtual void TimeFrameCome(object sender, MainWindow.TimeFrameEventArgs e)
        {
            this.AddInfoLog(e.MarketTime.Hour + ":" + e.MarketTime.Minute + ":" + e.MarketTime.Second + ":" + e.MarketTime.Millisecond);


            // Если таймфрейм стратегии не совпадает с таймфреймом по-умолчанию или стратегия не запущена, то ничего не делаем
            if ((e.MarketTime.AddSeconds(5).Minute) % TimeFrame.Minutes != 0 || ProcessState != ProcessStates.Started)
            {
                this.AddInfoLog("!!! Таймфрейм стратегии не совпадает с таймфреймом по-умолчанию или стратегия не запущена");
                return;
            }
                

            foreach (var security in from security in SecurityList let tempSecurity = security select security)
            {
                try
                {
                    this.AddInfoLog(
                        security.Code + ": " + "Open({0}), High({1}), Low({2}), Close({3}), Volume({4}), Delta({5})",
                        e.LastBarsDictionary[security.Code].Open, e.LastBarsDictionary[security.Code].High,
                        e.LastBarsDictionary[security.Code].Low, e.LastBarsDictionary[security.Code].Close,
                        e.LastBarsDictionary[security.Code].Volume, e.LastBarsDictionary[security.Code].Delta);
                }
                // ReSharper disable RedundantCatchClause
                catch (Exception)
                {

                    throw;
                }
            }
        }

        /// <summary>
        /// Обработчик события появления новых сделок (для реализации защиты по стопу)
        /// </summary>
        protected virtual void TraderNewTrades(IEnumerable<Trade> newTrades)
        {
            foreach (var newTrade in newTrades.Where(newTrade => SecurityVolumeDictionary.Any(item => newTrade.Security.Code == item.Key)))
            {
                // Обновляем Gross стратегии
                Gross += (newTrade.Price - LastTradesDictionary[newTrade.Security.Code]) *
                         PositionsDictionary[newTrade.Security.Code];

                // Обновляем информацию о цене последней сделки по активному инструменту
                LastTradesDictionary[newTrade.Security.Code] = newTrade.Price;

                // Если работаем не с Plaza, то более ничего не делаем
                if (MainWindow.Instance.ConnectorType != ConnectorTypes.Plaza) continue;

                foreach (var activeTrade in ActiveTrades)
                {
                    if (activeTrade.Security != newTrade.Security.Code || activeTrade.IsStopOrderPlaced) continue;

                    // Вычисляем есть ли ситуация стопа
                    var isStop = activeTrade.Direction == Direction.Buy
                                     ? newTrade.Price <= activeTrade.StopPrice
                                     : newTrade.Price >= activeTrade.StopPrice;

                    if(!isStop)
                        continue;

                    // Устанавливаем флаг, что стоп заявка размещена
                    activeTrade.IsStopOrderPlaced = true;

                    PlaceStopOrder(activeTrade, StopType);
                }
            }
        }

        /// <summary>
        /// Обработчик события совершения сделки
        /// </summary>
        protected override void OnNewMyTrades(IEnumerable<MyTrade> trades)
        {
            //Для каждого совершённого трейда
            foreach (var trade in trades)
            {
                // Передаем данные об изменившейся позиции в словарь
                PositionsDictionary[trade.Trade.Security.Code] = trade.Order.Direction == Sides.Buy
                                                                     ? PositionsDictionary[trade.Trade.Security.Code] +=
                                                                       trade.Trade.Volume
                                                                     : PositionsDictionary[trade.Trade.Security.Code] -=
                                                                       trade.Trade.Volume;

                if (!trade.Order.Comment.StartsWith(Name + ", enter")) continue;

                this.AddInfoLog("ВХОД - {0} , TranActionID - {1}", trade.Trade.Security.Code, trade.Trade.Id);

                var actTradeDirection = trade.Order.Direction == Sides.Buy ? Direction.Buy : Direction.Sell;
                var actStopPrice = actTradeDirection == Direction.Buy
                                       ? trade.Trade.Security.ShrinkPrice(trade.Trade.Price * (1 - StopLossPercent / 100))
                                       : trade.Trade.Security.ShrinkPrice(trade.Trade.Price * (1 + StopLossPercent / 100));


                var newActiveTrade = new ActiveTrade(trade.Trade.Id, trade.Trade.Security.Code, actTradeDirection,
                                                     trade.Trade.Price, trade.Trade.Volume, trade.Trade.Time, actStopPrice, trade.Order.Comment);
                // Добавляем информацию о сделке в коллекцию активных сделок, в том числе для того, чтобы отрабатывать СтопЛосс
                ActiveTrades.Add(newActiveTrade);

                // Вызываем событие прихода изменения ActiveTrades
                OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                // Регистрируем стопОрдер сразу, если работаем не с Plaza
                if (MainWindow.Instance.ConnectorType != ConnectorTypes.Plaza)
                    PlaceStopOrder(newActiveTrade, StopType);

                // Если установлен положительный тейкпрофит, то регистрируем профитОрдер
                if (TakeProfitPercent > 0)
                    PlaceProfitOrder(newActiveTrade);

                // Определяем время выхода из сделки через 2 свечки, если не сработал стоплосс
                if (BarsToClose <= 0) continue;

                var timeToClose = SetCloseTime(trade.Trade.Time, 2);

                // И включаем выход из сделки через заданное количество свечей
                trade.Trade.Security.WhenTimeCome(timeToClose).Do(() => ClosePositionByTime(newActiveTrade)).Apply(this);

                this.AddInfoLog("ВЫХОД:\nЗАЯВКА на ВЫХОД по ВРЕМЕНИ - {0}. Регистрируемся на выход по инструменту в {1}, Id сделки - {2}", trade.Trade.Security.Code, timeToClose.ToString(CultureInfo.InvariantCulture), trade.Trade.Id);
            }
        }

        protected enum StopTypes
        {
            /// <summary>
            /// Стоп по "рынку". По любой возможой цене.
            /// </summary>
            Market,
            /// <summary>
            /// Стоп по лучшей предлагаемой для нас цене в стакане c запасом в 0,15%.
            /// </summary>
            MarketLimitOfferForced,
            /// <summary>
            /// Стоп по лучшей предлагаемой для нас цене в стакане.
            /// </summary>
            MarketLimitOffer,
            /// <summary>
            /// Стоп в спред с запасом 0,5%. Практически, стоп по рынку, но по лимитированной заявке с глубоким запасом.
            /// </summary>
            MarketLimitForced,
            /// <summary>
            /// Стоп в спред с запасом 0,15%. Практически, стоп по рынку, но по лимитированной заявке с легким запасом.
            /// </summary>
            MarketLimitLight,
            /// <summary>
            /// Стоп по лучшей цене в стакане предлагаемой нашей стороной. Самый "легкий" стоп, но есть опасность не выйти из позиции долгое время.
            /// </summary>
            SpreadZero,
        }

        /// <summary>
        /// Метод установки стоп ордера по активной позиции
        /// </summary>
        protected virtual void PlaceStopOrder(ActiveTrade trade, StopTypes stopType)
        {
            var currentSecurity = SecurityList.First(sec => sec.Code == trade.Security);
            if(currentSecurity == null)
                return;

            decimal price;
            Order stopOrder;

            switch(MainWindow.Instance.ConnectorType)
            {
                case (ConnectorTypes.Quik):
                    price = trade.Direction == Direction.Sell
                                ? currentSecurity.ShrinkPrice(trade.StopPrice*(1 + 0.0015m))
                                : currentSecurity.ShrinkPrice(trade.StopPrice*(1 - 0.0015m));

                    stopOrder = new Order
                    {
                        Comment = Name + ",s," + trade.Id,

                        Portfolio = Portfolio,
                        Volume = trade.Volume,
                        Security = currentSecurity,
                        Type = OrderTypes.Conditional,
                        Direction =
                            trade.Direction == Direction.Sell
                                ? Sides.Buy
                                : Sides.Sell,

                        Price = price,

                        Condition = new QuikOrderCondition
                        {
                            Type = QuikOrderConditionTypes.StopLimit,
                            StopPrice = trade.StopPrice,
                        },
                    };

                    break;
                case (ConnectorTypes.Plaza):
                    switch (stopType)
                    {
                        case (StopTypes.Market):
                            price =
                                trade.Direction == Direction.Sell
                                    ? currentSecurity.ShrinkPrice(currentSecurity.MaxPrice)
                                    : currentSecurity.ShrinkPrice(currentSecurity.MinPrice);
                            break;
                        case (StopTypes.MarketLimitOfferForced):
                            price =
                                trade.Direction == Direction.Sell
                                    ? currentSecurity.ShrinkPrice(currentSecurity.BestAsk.Price * (1 + 0.0015m))
                                    : currentSecurity.ShrinkPrice(currentSecurity.BestBid.Price * (1 - 0.0015m));
                            break;
                        case (StopTypes.MarketLimitOffer):
                            price =
                                trade.Direction == Direction.Sell
                                    ? currentSecurity.BestAsk.Price
                                    : currentSecurity.BestBid.Price;
                            break;
                        case (StopTypes.MarketLimitForced):
                            price =
                                trade.Direction == Direction.Sell
                                    ? currentSecurity.ShrinkPrice(currentSecurity.BestBid.Price * (1 + 0.005m))
                                    : currentSecurity.ShrinkPrice(currentSecurity.BestAsk.Price * (1 - 0.005m));
                            break;
                        case (StopTypes.MarketLimitLight):
                            price =
                                trade.Direction == Direction.Sell
                                    ? currentSecurity.ShrinkPrice(currentSecurity.BestBid.Price * (1 + 0.0015m))
                                    : currentSecurity.ShrinkPrice(currentSecurity.BestAsk.Price * (1 - 0.0015m));
                            break;
                        case (StopTypes.SpreadZero):
                            price =
                                trade.Direction == Direction.Sell
                                    ? currentSecurity.BestBid.Price
                                    : currentSecurity.BestAsk.Price;
                            break;
                        default:
                            price =
                                trade.Direction == Direction.Sell
                                    ? currentSecurity.BestAsk.Price
                                    : currentSecurity.BestBid.Price;
                            break;
                    }

                    stopOrder = new Order
                    {
                        Comment = Name + ",s," + trade.Id,
                        Portfolio = Portfolio,
                        Type = OrderTypes.Limit,
                        Volume = trade.Volume,
                        Security = currentSecurity,
                        Direction =
                            trade.Direction == Direction.Sell
                                ? Sides.Buy
                                : Sides.Sell,

                        Price = price,
                    };
                    break;
                default:
                    price =
                        trade.Direction == Direction.Sell
                            ? currentSecurity.BestAsk.Price
                            : currentSecurity.BestBid.Price;

                    stopOrder = new Order
                    {
                        Comment = Name + ",s," + trade.Id,
                        Portfolio = Portfolio,
                        Type = OrderTypes.Limit,
                        Volume = trade.Volume,
                        Security = currentSecurity,
                        Direction =
                            trade.Direction == Direction.Sell
                                ? Sides.Buy
                                : Sides.Sell,

                        Price = price,
                    };
                    break;
            }

            // После срабатывания стопа, выводим сообщение в лог и отменяем встречные профит ордера

            stopOrder
                .WhenRegistered()
                .Once()
                .Do(() =>
                {
                    // Заносим данные о стоп заявке в объект активной сделки
                    trade.StopLossOrderTransactionId = stopOrder.TransactionId;
                    trade.StopLossOrderId = stopOrder.Id;

                    // Вызываем событие прихода изменения ActiveTrades
                    OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    this.AddInfoLog(
                        "ВЫХОД по СТОПУ - {0}. Зарегистрирована заявка на {1} на выход по стопу из сделки.",
                        trade.Security,
                        stopOrder.Direction == Sides.Buy ? "Продажу" : "Покупку");
                })
                .Apply(this);

            stopOrder
                .WhenNewTrades()
                .Do(newTrades =>
                {
                    //foreach (var newTrade in newTrades)
                    //{
                    //    foreach (var activeTrade in ActiveTrades.Where(activeTrade => activeTrade.Id == trade.Id))
                    //    {
                    //        activeTrade.Volume -= newTrade.Trade.Volume;

                    //        // Вызываем событие прихода изменения ActiveTrades
                    //        OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    //        if (activeTrade.Volume != 0)
                    //        {
                    //            this.AddInfoLog("Новый объем активной сделки с ID {0} - {1}",
                    //                  activeTrade.Id, activeTrade.Volume);
                    //        }
                    //        else
                    //        {
                    //            this.AddInfoLog("Новый объем активной сделки с ID {0} стал равен 0! Удаляем активную сделку и отменяем соответствующие заявки",
                    //                  activeTrade.Id);
                    //        }
                    //    }
                    //}
                })
                .Apply(this);


            stopOrder
                .WhenMatched()
                .Once()
                .Do(() =>
                {
                    // Обновляем список активных трейдов. Точнее, удаляем закрывшийся по стопу трейд.
                    ActiveTrades = ActiveTrades.Where(activeTrade => activeTrade != trade).ToList();

                    // И вызываем событие прихода изменения ActiveTrades
                    OnActiveTradesChanged(new ActiveTradesChangedEventArgs());


                    var ordersToCancel = Connector.Orders.Where(
                        order => order!= null &&
                        ((order.Comment.EndsWith(trade.Id.ToString(CultureInfo.CurrentCulture)) &&
                          order.State == OrderStates.Active)));

                    //Если нет других активных ордеров связанных с данным активным трейдом, то ничего не делаем
                    if(!ordersToCancel.Any())
                        return;
                    
                    // Иначе удаляем все связанные с данным активным трейдом ордера
                    foreach (var order in ordersToCancel)
                    {
                        Connector.CancelOrder(order);
                    }

                    this.AddInfoLog("ВЫХОД по СТОПУ - {0}", trade.Security);
                })
                .Apply(this);

            // Регистрируем стоп ордер
            RegisterOrder(stopOrder);
        }

        /// <summary>
        /// Метод установки профит ордера по активной позиции
        /// </summary>
        protected virtual void PlaceProfitOrder(ActiveTrade trade)
        {
            var currentSecurity = SecurityList.First(sec => sec.Code == trade.Security);
            if(currentSecurity == null)
                return;

            var profitOrder = new Order
            {
                Comment = Name + ",p," + trade.Id,
                Portfolio = Portfolio,
                Type = OrderTypes.Limit,
                Volume = trade.Volume,
                Security = currentSecurity,
                Direction =
                    trade.Direction == Direction.Sell
                        ? Sides.Buy
                        : Sides.Sell,
                Price = trade.Direction == Direction.Sell
                                  ? currentSecurity.ShrinkPrice((trade.Price * (1 - TakeProfitPercent / 100)))
                                  : currentSecurity.ShrinkPrice(trade.Price * (1 + TakeProfitPercent / 100))
            };

            profitOrder
                .WhenRegistered()
                .Once()
                .Do(() =>
                {
                    trade.ProfitOrderTransactionId = profitOrder.TransactionId;
                    trade.ProfitOrderId = profitOrder.Id;

                    // Вызываем событие прихода изменения ActiveTrades
                    OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    this.AddInfoLog(
                                    "ТЕЙКПРОФИТ - {0}. Зарегистрирована заявка на {1} на выход по тейк профиту",
                                    trade.Security,
                                    trade.Direction == Direction.Buy ? "Продажу" : "Покупку");
                })
                .Apply(this);

            profitOrder
                .WhenNewTrades()
                .Do(newTrades =>
                {
                    //foreach (var newTrade in newTrades)
                    //{
                    //    foreach (var activeTrade in ActiveTrades.Where(activeTrade => activeTrade.Id == trade.Id))
                    //    {
                    //        activeTrade.Volume -= newTrade.Trade.Volume;

                    //        // Вызываем событие прихода изменения ActiveTrades
                    //        OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    //        if (activeTrade.Volume != 0)
                    //        {
                    //            this.AddInfoLog("Новый объем активной сделки с ID {0} - {1}",
                    //                  activeTrade.Id, activeTrade.Volume);
                    //        }
                    //        else
                    //        {
                    //            this.AddInfoLog("Новый объем активной сделки с ID {0} стал равен 0! Удаляем активную сделку и отменяем соответствующие заявки",
                    //                  activeTrade.Id);
                    //        }
                    //    }
                    //}
                })
                .Apply(this);

            profitOrder
                .WhenMatched()
                .Once()
                .Do(() =>
                {
                    // Обновляем список активных трейдов. Точнее, удаляем закрывшийся по профиту трейд.
                    ActiveTrades = ActiveTrades.Where(activeTrade => activeTrade != trade).ToList();

                    // Вызываем событие прихода изменения ActiveTrades
                    OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    var ordersToCancel = Connector.Orders.Where(
                        order => order != null &&
                        ((order.Comment.EndsWith(trade.Id.ToString(CultureInfo.CurrentCulture)) &&
                          order.State == OrderStates.Active)));

                    //Если нет других активных ордеров связанных с данным активным трейдом, то ничего не делаем
                    if (!ordersToCancel.Any())
                        return;

                    // Иначе удаляем все связанные с данным активным трейдом ордера
                    foreach (var order in ordersToCancel)
                    {
                        Connector.CancelOrder(order);
                    }

                    this.AddInfoLog("ВЫХОД по ПРОФИТУ - {0}", trade.Security);
                })
                .Apply(this);
            
            // Регистрируем профит ордер
            RegisterOrder(profitOrder);
        }

        /// <summary>
        /// Метод определяем времени закрытия сделки, если не сработал стоплосс
        /// </summary>
        protected virtual DateTime SetCloseTime(DateTime timeFrom, int bars)
        {
            int outReminder;
            var summable = (int)TimeFrame.TotalMinutes * (bars + 1);

            var timeToClose = timeFrom.Subtract(TimeSpan.FromSeconds(timeFrom.Second));

            Math.DivRem(timeToClose.Minute, (int)TimeFrame.TotalMinutes, out outReminder);
            timeToClose = timeToClose.Subtract(TimeSpan.FromMinutes(outReminder));
            timeToClose = timeToClose.Add(TimeSpan.FromMinutes(summable));

            // Если попадаем в вечерний клиринг, то
            if (timeToClose.Hour == 18)
            {
                switch (timeToClose.Minute)
                {
                    case 45:
                        timeToClose = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 18, 42, 00);
                        break;
                    case 50:
                        timeToClose = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 19, 5, 00);
                        break;
                    case 55:
                        timeToClose = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 19, 10, 00);
                        break;
                }
            }

            if (timeToClose.Hour == 14 && timeToClose.Minute == 00)
            {
                timeToClose = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 14, 5, 0);
            }

            return timeToClose;
        }

        /// <summary>
        /// Метод обработки события прихода времени для закрытия сделки, если не сработал стоплосс
        /// </summary>
        protected virtual void ClosePositionByTime(ActiveTrade trade)
        {
            // Если коллекция активных сделок не содержит трейд, то временной ордер не должен отрабатываться
            if (ActiveTrades.All(activeTrade => activeTrade != trade))
                return;

            var currentSecurity = SecurityList.First(sec => sec.Code == trade.Security);
            if(currentSecurity == null)
                return;

            //Устанавливаем параметры временного ордера
            var closeByTimeOrder = new Order
            {
                Comment = Name + ",t," + trade.Id,
                Type = OrderTypes.Limit,
                Portfolio = Portfolio,
                Security = currentSecurity,
                Volume = trade.Volume,
                Direction =
                    trade.Direction == Direction.Sell
                        ? Sides.Buy
                        : Sides.Sell,
                Price =
                    trade.Direction == Direction.Sell
                        ? currentSecurity.ShrinkPrice(currentSecurity.BestBid.Price * (1 + 0.001m))
                        : currentSecurity.ShrinkPrice(currentSecurity.BestAsk.Price * (1 - 0.001m)),
            };

            // После регистрации временного ордера, выводим сообщение в лог

            closeByTimeOrder
                .WhenRegistered()
                .Once()
                .Do(() => this.AddInfoLog(
                        "ВЫХОД по ВРЕМЕНИ - {0}. Зарегистрирована заявка на выход из сделки впереди лучшей цены {1} в стакане.",
                        trade.Security,
                        closeByTimeOrder.Direction == Sides.Buy ? "Bid" : "Ask"))
                .Apply(this);

            // При появлении сделок по временному ордеру выводим в лог сообщении об изменении объема активной сделки
            closeByTimeOrder
                .WhenNewTrades()
                .Do(newTrades =>
                {
                    //foreach (var newTrade in newTrades)
                    //{
                    //    foreach (var activeTrade in ActiveTrades.Where(activeTrade => activeTrade.Id == trade.Id))
                    //    {
                    //        activeTrade.Volume -= newTrade.Trade.Volume;

                    //        // Вызываем событие прихода изменения ActiveTrades
                    //        OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    //        if (activeTrade.Volume != 0)
                    //        {
                    //            this.AddInfoLog("Новый объем активной сделки с ID {0} - {1}",
                    //                  activeTrade.Id, activeTrade.Volume);
                    //        }
                    //        else
                    //        {
                    //            this.AddInfoLog("Новый объем активной сделки с ID {0} стал равен 0! Удаляем активную сделку и отменяем соответствующие заявки",
                    //                  activeTrade.Id);
                    //        }
                    //    }
                    //}
                })
                .Apply(this);

            // После срабатывания временного ордера, выводим сообщение в лог и останавливаем защитную стратегию
            closeByTimeOrder
                .WhenMatched()
                .Do(() =>
                {
                    ActiveTrades = ActiveTrades.Where(activeTrade => activeTrade != trade).ToList();

                    // Вызываем событие прихода изменения ActiveTrades
                    OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    var ordersToCancel = Connector.Orders.Where(
                        order => order != null &&
                        ((order.Comment.EndsWith(trade.Id.ToString(CultureInfo.CurrentCulture)) &&
                          order.State == OrderStates.Active)));

                    //Если нет других активных ордеров связанных с данным активным трейдом, то ничего не делаем
                    if (!ordersToCancel.Any())
                        return;

                    // Иначе удаляем все связанные с данным активным трейдом ордера
                    foreach (var order in ordersToCancel)
                    {
                        Connector.CancelOrder(order);
                    }

                    this.AddInfoLog(
                        "ВЫХОД по ВРЕМЕНИ - {0}. Вышли из сделки впереди лучшей цены {1} в стакане.",
                        trade.Security,
                        closeByTimeOrder.Direction == Sides.Buy ? "Bid" : "Ask");
                })
                        .Apply(this);

            RegisterOrder(closeByTimeOrder);
        }

        /// <summary>
        /// Определение текущей позиции по инструменту
        /// </summary>
        public decimal GetCurrentPosition(Security security)
        {
            var position = PositionsDictionary[security.Code];

            return position;
        }

        /// <summary>
        /// Метод для сохранения датасетов в файл подготовленный для WealthLab
        /// </summary>
        public void SaveChartsToFiles()
        {
            if (InfoToEmail == null)
                InfoToEmail = new List<string>();

            if (InfoToEmail.Count != 0)
                InfoToEmail.Clear();

            var currentDate = "";

            if (DateTime.Now.Month < 10 && DateTime.Now.Day >= 10)
            {
                currentDate = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture) + "0" +
                              DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) +
                              DateTime.Now.Day.ToString(CultureInfo.InvariantCulture);
            }
            if (DateTime.Now.Month < 10 && DateTime.Now.Day < 10)
            {
                currentDate = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture) + "0" +
                              DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) + "0" +
                              DateTime.Now.Day.ToString(CultureInfo.InvariantCulture);
            }
            if (DateTime.Now.Month >= 10 && DateTime.Now.Day < 10)
            {
                currentDate = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture) +
                              DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) + "0" +
                              DateTime.Now.Day.ToString(CultureInfo.InvariantCulture);
            }
            if (DateTime.Now.Month >= 10 && DateTime.Now.Day >= 10)
            {
                currentDate = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture) +
                              DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) +
                              DateTime.Now.Day.ToString(CultureInfo.InvariantCulture);
            }

            foreach (var security in SecurityList)
            {
                foreach (var bar in MainWindow.Instance.ChartsDictionary[security.Code].Bars)
                {
                    var tempMinutes = bar.Time.AddSeconds(1).Minute.ToString(CultureInfo.InvariantCulture).Length == 2
                                          ? bar.Time.AddSeconds(1).Minute.ToString(CultureInfo.InvariantCulture)
                                          : "0" + bar.Time.AddSeconds(1).Minute;

                    InfoToEmail.Add(currentDate + " " +
                                    bar.Time.AddSeconds(1).Hour + ":" + tempMinutes + ":00" + " " + bar.Open.ToString(CultureInfo.InvariantCulture) + " " +
                                    bar.High.ToString(CultureInfo.InvariantCulture) + " " +
                                    bar.Low.ToString(CultureInfo.InvariantCulture) + " " +
                                    bar.Close.ToString(CultureInfo.InvariantCulture) + " " +
                                    bar.Volume);
                }

                File.WriteAllLines(security.Code + "_" + currentDate + ".txt", InfoToEmail);

                InfoToEmail.Clear();
            }
        }

        /// <summary>
        /// Метод отправки почтового сообщения
        /// </summary>
        public static void SendMail(string smtpServer, string from, string password, string mailto, string caption, string message, string attachFile = null)
        {
            try
            {
                var mail = new MailMessage { From = new MailAddress(@from) };
                mail.To.Add(new MailAddress(mailto));
                mail.Subject = caption;
                mail.Body = message;
                if (!string.IsNullOrEmpty(attachFile))
                    mail.Attachments.Add(new Attachment(attachFile));

                var client = new SmtpClient
                {
                    Host = smtpServer,
                    Port = 587,
                    EnableSsl = true,
                    Credentials = new NetworkCredential(@from.Split('@')[0], password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };
                client.Send(mail);
                mail.Dispose();
            }
            catch (Exception e)
            {
                throw new Exception("Mail.Send: " + e.Message);
            }
        }

        /// <summary>
        /// Метод подготовки E-mail для отправки
        /// </summary>
        public bool PrepareInfoToEmail()
        {
            if (MyTrades.Count(myTrade => myTrade.Trade.Time >= LastTimeEmail) == 0)
                return false;

            if (InfoToEmail == null)
                InfoToEmail = new List<string>();

            if (InfoToEmail.Count != 0)
                InfoToEmail.Clear();

            // Создаем шапку отчета
            InfoToEmail.Add("\nInformation on transactions " + Name + ":");
            InfoToEmail.Add("\n");

            // Отфильтровываем из соответствующей коллекции только новые сделки
            var newMyTrades = MyTrades.Where(myTrade => myTrade.Trade.Time >= LastTimeEmail);

            // Группируем ссылки на MyTrade по коду инструмента
            var myTradesBySec =
                    newMyTrades.GroupBy(myTrade => myTrade.Trade.Security.Code);

            // В пределах одного кода инструмента группируем ссылки MyTrade по времени и выдаем по ним информацию в коллекцию _infoToEmail
            foreach (var bySec in myTradesBySec)
            {
                var bySecOrderByTime = bySec.OrderBy(mytrade => mytrade.Trade.Time);
                foreach (var trade in bySecOrderByTime)
                {
                    var dir = trade.Order.Direction == Sides.Buy ? "BUY" : "SELL";
                    InfoToEmail.Add("Trade time: " + trade.Trade.Time);
                    InfoToEmail.Add(dir + " " + trade.Trade.Security.Code + " at the price of " + trade.Trade.Price + ", volume - " + trade.Trade.Volume);
                }
            }

            SaveMyTradesToFile();

            return true;
        }

        /// <summary>
        /// Метод сохранения сделок в файл
        /// </summary>
        private void SaveMyTradesToFile()
        {
            var currentDate = DateTime.Now.Day.ToString(CultureInfo.InvariantCulture) + "-" +
                              DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) + "-" +
                              DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);

            File.WriteAllLines(currentDate + "_" + Name + ".txt", InfoToEmail);
        }

        /// <summary>
        /// Статичный метод для отправки E-mail
        /// </summary>
        public void SendInfoToEmail()
        {
            var currentDate = DateTime.Now.Day.ToString(CultureInfo.InvariantCulture) + "-" +
                              DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) + "-" +
                              DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);

            SendMail("smtp.gmail.com", "izixhrockyrobo@gmail.com", "gpv6ta&b?m9T", " lipot@mail.ru",
                     Name + " report",
                     "", currentDate + "_" + Name + ".txt");

            SendMail("smtp.gmail.com", "izixhrockyrobo@gmail.com", "gpv6ta&b?m9T", " rockybeat@rambler.ru",
                     Name + " report",
                     "", currentDate + "_" + Name + ".txt");
        }

        /// <summary>
        /// Комплексная процедура по подготовке и отправке почты в момент клирингов
        /// </summary>
        private void PrepareAndSendMail(bool isEveningClearing)
        {
            try
            {
                if (IsWorkContour)          // Если контур рабочий
                {
                    // Подготавливаем информацию о сделках
                    if (PrepareInfoToEmail())
                        SendInfoToEmail();                  // Отправляем E-mail
                }
                else
                {                           // Если контур тестовый
                    // Подготавливаем информацию о сделках
                    if (PrepareInfoToEmail())
                    {
                        var currentDate = DateTime.Now.Day.ToString(CultureInfo.InvariantCulture) + "-" +
                          DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) + "-" +
                          DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);

                        SendMail("smtp.gmail.com", "izixhrockyrobo@gmail.com", "gpv6ta&b?m9T", " lipot@mail.ru",
                                 Name + " report (Test)",
                                 "", currentDate + "_" + Name + ".txt");
                    }
                }

                // Обновляем информацию о времени последнего отправления E-mail
                LastTimeEmail = DateTime.Now;

                if (IsIntraDay || !isEveningClearing) return;

                switch (DateTime.Today.AddDays(1).DayOfWeek)
                {
                    case (DayOfWeek.Saturday):
                        _timeToSendMailInDayClearing = DateTime.Today.AddHours(14).AddMinutes(01).AddDays(3);
                        _timeToSendMailInEveningClearing = DateTime.Today.AddHours(18).AddMinutes(51).AddDays(3);
                        break;

                    case (DayOfWeek.Sunday):
                        _timeToSendMailInDayClearing = DateTime.Today.AddHours(14).AddMinutes(01).AddDays(2);
                        _timeToSendMailInEveningClearing = DateTime.Today.AddHours(18).AddMinutes(51).AddDays(2);
                        break;

                    default:
                        _timeToSendMailInDayClearing = DateTime.Today.AddHours(14).AddMinutes(01).AddDays(1);
                        _timeToSendMailInEveningClearing = DateTime.Today.AddHours(18).AddMinutes(51).AddDays(1);
                        break;
                }

                Security
                .WhenTimeCome(_timeToSendMailInDayClearing)
                .Once()
                .Do(() => PrepareAndSendMail(false))
                .Apply(this);

                Security
                    .WhenTimeCome(_timeToSendMailInEveningClearing)
                    .Once()
                    .Do(() => PrepareAndSendMail(true))
                    .Apply(this);

                this.AddInfoLog("Время отправки писем с отчетами:\nУтро - " + _timeToSendMailInDayClearing + "\nВечер - " +
                            _timeToSendMailInEveningClearing);
            }
            catch (Exception e)
            {
                this.AddInfoLog("Не удалось отправить E-mail: " + e.Message);
            }
        }

        /// <summary>
        /// Комплексная процедура по подготовке и отправке почты в момент остановки стратегии
        /// </summary>
        private void PrepareAndSendMailOnStop()
        {
            // Если остановка не по расписанию (в ручном режиме), то ничего не отправляем
            if (!_isStopRobotByShedule) return;

            if (IsWorkContour)      // Если контур рабочий
            {
                // Подготавливаем информацию о сделках
                if (PrepareInfoToEmail())
                    // Отправляем E-mail
                    SendInfoToEmail();
            }
            else
            {                       // Если контур тестовый
                // Подготавливаем информацию о сделках
                if (PrepareInfoToEmail())
                {
                    var currentDate = DateTime.Now.Day.ToString(CultureInfo.InvariantCulture) + "-" +
                                      DateTime.Now.Month.ToString(CultureInfo.InvariantCulture) + "-" +
                                      DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);

                    SendMail("smtp.gmail.com", "izixhrockyrobo@gmail.com", "gpv6ta&b?m9T", " lipot@mail.ru",
                             Name + " report (Test)",
                             "", currentDate + "_" + Name + ".txt");
                }

            }

            // Обновляем информацию о времени последнего отправления E-mail
            LastTimeEmail = DateTime.Now;

            // Сбрасываем булеву переменную в начальное состояние перед остановкой робота
            _isStopRobotByShedule = false;
        }

        /// <summary>
        /// Закрытие всех открытых позиций по рынку (не важно по какой цене)
        /// </summary>
        public void CloseAllPosition()
        {
            foreach (var security in SecurityList)
            {
                ExitAtMarket(security);
            }
        }

        /// <summary>
        /// Закрытие по рынку (не важно по какой цене). Быть остородным пр применении для низколиквидных инструментов.
        /// </summary>
        virtual protected void ExitAtMarket(Security security)
        {
            var currentPosition = GetCurrentPosition(security);
            if (currentPosition == 0)
                return;

            var volume = Math.Abs(currentPosition);

            var newExitOrder = new Order
                                   {
                                       Comment = Name + ",m",
                                       Type = OrderTypes.Limit,
                                       Portfolio = Portfolio,
                                       Security = security,
                                       Volume = volume,
                                       Direction = currentPosition > 0 ? Sides.Sell : Sides.Buy,
                                       Price = currentPosition > 0
                                                   ? security.ShrinkPrice(security.BestBid.Price * (1 - 0.002m))
                                                   : security.ShrinkPrice(security.BestAsk.Price * (1 + 0.002m)),
                                   };

            newExitOrder
                .WhenRegistered()
                .Once()
                .Do(() => this.AddInfoLog(
                        "ВЫХОД по МАРКЕТУ - {0}. Зарегистрирована заявка на выход из позиции по максимальному {1} в стакане.",
                        security,
                        newExitOrder.Direction == Sides.Buy ? "Bid" : "Ask"))
                .Apply(this);

            newExitOrder
                .WhenMatched()
                .Do(() =>
                {
                    ActiveTrades =
                        ActiveTrades.Where(activeTrade => activeTrade.Security != security.Code).ToList();

                    // Вызываем событие прихода изменения ActiveTrades
                    OnActiveTradesChanged(new ActiveTradesChangedEventArgs());

                    this.AddInfoLog(
                        "ВЫХОД по МАРКЕТУ - {0}. Вышли из позиции по МАРКЕТУ.", security.Code);
                })
                .Apply(this);

            // Регистрируем ордер

            this.AddInfoLog("Регистрируем ордер на выход по МАРКЕТУ");
            RegisterOrder(newExitOrder);
        }

        /// <summary>
        /// Остановка робота
        /// </summary>
        public void StopRobot()
        {
            if (CloseAllPositionsOnStop == false || SecurityList.All(security => GetCurrentPosition(security) == 0))
            {
                PrepareAndSendMailOnStop();

                Stop();
                return;
            }

            PositionsChanged -= MyBaseStrategyPositionsChanged;
            PositionsChanged += MyBaseStrategyPositionsChanged;

            CloseAllPosition();
        }

        /// <summary>
        /// Обработчик события изменения позиции стратегии
        /// </summary>
        void MyBaseStrategyPositionsChanged(IEnumerable<Position> obj)
        {
            if (SecurityList.Any(security => GetCurrentPosition(security) != 0)) return;
            if (ActiveTrades.Count != 0)
            {
                ActiveTrades.Clear();

                // Вызываем событие прихода изменения ActiveTrades
                OnActiveTradesChanged(new ActiveTradesChangedEventArgs());
            }

            PrepareAndSendMailOnStop();

            Stop();
        }

        /// <summary>
        /// Метод сериализации активных сделок, также сериализует словарь Position
        /// </summary>
        protected void SaveActiveTradesAndPositions()
        {
            using (Stream output = File.Create(Name + "ActiveTrades.bin"))
            {
                var bf = new BinaryFormatter();

                bf.Serialize(output, ActiveTrades);
            }

            using (Stream output = File.Create(Name + "PositionsDict.bin"))
            {
                var bf = new BinaryFormatter();

                bf.Serialize(output, PositionsDictionary);
            }
        }

        /// <summary>
        /// Метод загрузки сохраненных ActiveTrades, также подгружает словарь Position
        /// </summary>
        protected void LoadActiveTrades(string nameOfStrategy)
        {
            var bf = new BinaryFormatter();

            using (Stream activeTradesStream = File.OpenRead(nameOfStrategy + "ActiveTrades.bin"))
            {
                ActiveTrades = (List<ActiveTrade>)bf.Deserialize(activeTradesStream);
            }

            using (Stream positionStream = File.OpenRead(nameOfStrategy + "PositionsDict.bin"))
            {
                PositionsDictionary = (Dictionary<string, decimal>)bf.Deserialize(positionStream);
            }

            // Обновить словарь последних сделок при загрузке активных сделок
            foreach (var trade in ActiveTrades)
            {
                LastTradesDictionary[trade.Security] = trade.Price;
            }
        }

        /// <summary>
        /// Разместить тестовый ордер
        /// </summary>
        public void PlaceTestOrder()
        {
            var currentSecurity = SecurityList.First();

            MessageBox.Show(currentSecurity.Code);

            var mbr1 =
                        MessageBox.Show("Выставить тестовый ордер на покупку?", "Параметры стратегии", MessageBoxButton.YesNoCancel);

            switch (mbr1)
            {
                case (MessageBoxResult.Yes):
                    var orderBuy = new Order
                    {
                        Comment = Name + ", enter",
                        Portfolio = Portfolio,
                        Security = currentSecurity,
                        Type = OrderTypes.Limit,
                        Volume = 1,
                        Direction = Sides.Buy,
                        Price = currentSecurity.BestAsk.Price
                    };

                    this.AddInfoLog(
                        "ТЕСТ. ЗАЯВКА на ВХОД - {0}. Регистрируем заявку на {1} по цене {2} c объемом {3} - стоп на {4}",
                        currentSecurity.Code, orderBuy.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderBuy.Price.ToString(CultureInfo.InvariantCulture),
                        orderBuy.Volume.ToString(CultureInfo.InvariantCulture),
                        currentSecurity.ShrinkPrice(orderBuy.Price * (1 - StopLossPercent / 100)));

                    var orderBuy2 = new Order
                    {
                        Comment = Name + ", enter2",
                        Portfolio = Portfolio,
                        Security = currentSecurity,
                        Type = OrderTypes.Limit,
                        Volume = 1,
                        Direction = Sides.Buy,
                        Price = currentSecurity.BestAsk.Price
                    };

                    this.AddInfoLog(
                        "ТЕСТ. ЗАЯВКА на ВХОД - {0}. Регистрируем заявку №2 на {1} по цене {2} c объемом {3} - стоп на {4}",
                        currentSecurity.Code, orderBuy2.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderBuy2.Price.ToString(CultureInfo.InvariantCulture),
                        orderBuy2.Volume.ToString(CultureInfo.InvariantCulture),
                        currentSecurity.ShrinkPrice(orderBuy2.Price * (1 - StopLossPercent / 100)));

                    var orderBuy3 = new Order
                    {
                        Comment = Name + ", enter3",
                        Portfolio = Portfolio,
                        Security = currentSecurity,
                        Type = OrderTypes.Limit,
                        Volume = 1,
                        Direction = Sides.Buy,
                        Price = currentSecurity.BestAsk.Price
                    };

                    this.AddInfoLog(
                        "ТЕСТ. ЗАЯВКА на ВХОД - {0}. Регистрируем заявку №3 на {1} по цене {2} c объемом {3} - стоп на {4}",
                        currentSecurity.Code, orderBuy3.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderBuy3.Price.ToString(CultureInfo.InvariantCulture),
                        orderBuy3.Volume.ToString(CultureInfo.InvariantCulture),
                        currentSecurity.ShrinkPrice(orderBuy3.Price * (1 - StopLossPercent / 100)));

                    RegisterOrder(orderBuy);
                    RegisterOrder(orderBuy2);
                    RegisterOrder(orderBuy3);
                    break;
                    
                case (MessageBoxResult.No):
                    var orderSell = new Order
                    {
                        Comment = Name + ", enter",
                        Portfolio = Portfolio,
                        Security = Security,
                        Type = OrderTypes.Limit,
                        Volume = 1,
                        Direction = Sides.Sell,
                        Price = Security.BestBid.Price
                    };

                    this.AddInfoLog(
                        "ТЕСТ. ЗАЯВКА на ВХОД - {0}. Регистрируем заявку на {1} по цене {2} c объемом {3} - стоп на {4}",
                        Security.Code, orderSell.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderSell.Price.ToString(CultureInfo.InvariantCulture),
                        orderSell.Volume.ToString(CultureInfo.InvariantCulture),
                        Security.ShrinkPrice(orderSell.Price * (1 + StopLossPercent / 100)));

                    var orderSell2 = new Order
                    {
                        Comment = Name + ", enter2",
                        Portfolio = Portfolio,
                        Security = Security,
                        Type = OrderTypes.Limit,
                        Volume = 1,
                        Direction = Sides.Sell,
                        Price = Security.BestBid.Price
                    };

                    this.AddInfoLog(
                        "ТЕСТ. ЗАЯВКА на ВХОД - {0}. Регистрируем заявку №2 на {1} по цене {2} c объемом {3} - стоп на {4}",
                        Security.Code, orderSell2.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderSell2.Price.ToString(CultureInfo.InvariantCulture),
                        orderSell2.Volume.ToString(CultureInfo.InvariantCulture),
                        Security.ShrinkPrice(orderSell2.Price * (1 + StopLossPercent / 100)));

                    var orderSell3 = new Order
                    {
                        Comment = Name + ", enter3",
                        Portfolio = Portfolio,
                        Security = Security,
                        Type = OrderTypes.Limit,
                        Volume = 1,
                        Direction = Sides.Sell,
                        Price = Security.BestBid.Price
                    };

                    this.AddInfoLog(
                        "ТЕСТ. ЗАЯВКА на ВХОД - {0}. Регистрируем заявку на {1} по цене {2} c объемом {3} - стоп на {4}",
                        Security.Code, orderSell3.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderSell3.Price.ToString(CultureInfo.InvariantCulture),
                        orderSell3.Volume.ToString(CultureInfo.InvariantCulture),
                        Security.ShrinkPrice(orderSell3.Price * (1 + StopLossPercent / 100)));

                    RegisterOrder(orderSell);
                    RegisterOrder(orderSell2);
                    RegisterOrder(orderSell3);
                    break;

                case (MessageBoxResult.Cancel):
                    break;
            }
        }
    }
}
