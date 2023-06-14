using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Logging;
using StockSharp.Messages;

namespace PrismaBoy
{
    sealed class Vol : MyBaseStrategy
    {
        /// <summary>
        /// Длина объема
        /// </summary>
        private readonly int _length;

        /// <summary>
        /// Множитель объема
        /// </summary>
        private readonly int _factor;

        /// <summary>
        /// Размер свечи объема, %
        /// </summary>
        private readonly decimal _candleSizePercent;

        /// <summary>
        /// Тело свечи объема, %
        /// </summary>
        private readonly decimal _bodyPercent;

        /// <summary>
        /// Объемы за последние _length свечей
        /// </summary>
        private List<decimal>  _lastVolumes;

        /// <summary>
        /// Конструктор класса BreakDown
        /// </summary>
        public Vol(List<Security> securityList, Dictionary<string, decimal> securityVolumeDictionary, TimeSpan timeFrame, decimal stopLossPercent, decimal takeProfitPercent, int length, int factor, decimal candleSizePercent, decimal bodyPercent, bool loadActiveTrades)
            : base(securityList, securityVolumeDictionary, timeFrame, stopLossPercent, takeProfitPercent)
        {
            Name = "Vol";
            IsIntraDay = false;
            CloseAllPositionsOnStop = false;
            CancelOrdersWhenStopping = false;
            StopType = StopTypes.MarketLimitOfferForced;

            // В соответствии с параметрами конструктора
            _length = length;
            _factor = factor;
            _candleSizePercent = candleSizePercent;
            _bodyPercent = bodyPercent;

            if (loadActiveTrades)
            {
                LoadActiveTrades(Name);
            }
        }

        /// <summary>
        /// Событие старта стратегии
        /// </summary>
        protected override void OnStarted()
        {
            TimeToStopRobot = IsWorkContour
                                  ? new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 23,
                                                 50, 00)
                                  : new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 23,
                                                 49, 00);
            
            this.AddInfoLog("Стратегия запускается со следующими параметрами:" +
                            "\nТаймфрейм: " + TimeFrame +
                            "\nДлина объема" + _length +
                            "\nМножитель объема" + _factor +
                            "\nРазмер свечи объема, %" + _candleSizePercent +
                            "\nТело свечи объема, %: " + _bodyPercent +
                            "\nСтоплосс, %: " + StopLossPercent +
                            "\nТейкпрофит, %: " + TakeProfitPercent);

            base.OnStarted();
        }

        /// <summary>
        /// Метод-обработчик прихода новой свечки
        /// </summary>
        protected override void TimeFrameCome(object sender, MainWindow.TimeFrameEventArgs e)
        {
            base.TimeFrameCome(sender, e);

            #region Фильтр времени

            // В числовом формате (кол-во минут от начала дня)
            var currentTime = e.MarketTime.AddSeconds(5).Minute + e.MarketTime.AddSeconds(5).Hour * 60;

            // Если сейчас раньше, чем длина объема, то выходим
            if(currentTime < 600 + TimeFrame.Minutes * _length)
                return;

            #endregion

            // Если сейчас позже, чем длина объема, то действуем
            foreach (var security in SecurityList)
            {
                var currentSecurity = security;

                // Если нет активных трейдов по инструменту
                if (ActiveTrades.Count(trade => trade.Security == currentSecurity.Code) != 0) continue;
                
                // Если есть уже выставленные активные заявки на вход, то отменяем их
                if (Orders.Any())
                {
                    foreach (var order in Orders.Where(order => order.Security == currentSecurity).Where(order => order.State == OrderStates.Active && order.Comment.EndsWith("enter")).Where(order => order != null))
                    {
                        Connector.CancelOrder(order);
                    }
                }

                #region Фильтр размера свечи объема

                decimal candleSize = 0;
                if (e.LastBarsDictionary[currentSecurity.Code].Close != 0)
                    candleSize =
                        Math.Round(Math.Abs(((e.LastBarsDictionary[currentSecurity.Code].Close -
                                              e.LastBarsDictionary[currentSecurity.Code].Open)/
                                             e.LastBarsDictionary[currentSecurity.Code].Close))*100, 2);

                this.AddInfoLog("РАЗМЕР свечи, % - {0}", candleSize.ToString(CultureInfo.InvariantCulture));

                var isCandleBig = candleSize > _candleSizePercent;

                #endregion

                #region Фильтр тени объема

                decimal bodyPercent = 0;
                if (e.LastBarsDictionary[currentSecurity.Code].High - e.LastBarsDictionary[currentSecurity.Code].Low !=
                    0)
                    bodyPercent = Math.Round(Math.Abs(((e.LastBarsDictionary[currentSecurity.Code].Close -
                                                        e.LastBarsDictionary[currentSecurity.Code].Open)/
                                                       (e.LastBarsDictionary[currentSecurity.Code].High -
                                                        e.LastBarsDictionary[currentSecurity.Code].Low)))*100, 0);

                this.AddInfoLog("ТЕЛО к размаху свечи, % - {0}", bodyPercent.ToString(CultureInfo.InvariantCulture));

                var isShadowSmall = bodyPercent > _bodyPercent;

                #endregion

                #region Фильтр величины объема

                var isVolumeBig = false;

                if (MainWindow.Instance.ChartsDictionary[currentSecurity.Code].Bars.Count > _length)
                {
                    var currentVolume = MainWindow.Instance.ChartsDictionary[currentSecurity.Code].Bars.Peek().Volume;

                    if (_lastVolumes == null)
                        _lastVolumes = new List<decimal>();

                    if (_lastVolumes.Count != 0)
                        _lastVolumes.Clear();
                    
                    for (var i = 0; i < _length; i++)
                    {
                        this.AddInfoLog("Объем[{0}] - {1}", i + 1, MainWindow.Instance.ChartsDictionary[currentSecurity.Code].Bars.ElementAt(i + 1).Volume);
                        _lastVolumes.Add(MainWindow.Instance.ChartsDictionary[currentSecurity.Code].Bars.ElementAt(i + 1).Volume);
                    }

                    var currentAverageVolume = _lastVolumes.Average();

                    this.AddInfoLog("Объем - {0}\nСредний объем - {1}",
                                    currentVolume.ToString(CultureInfo.InvariantCulture),
                                    Math.Round(currentAverageVolume, 0).ToString(CultureInfo.InvariantCulture));

                    isVolumeBig = currentVolume > currentAverageVolume * _factor;
                }

                #endregion

                if (!isShadowSmall || !isCandleBig || !isVolumeBig) continue;

                this.AddInfoLog("ВХОД. Условия свечки подходят. Выставляем заявку на вход.");

                // Если свечка и большая, и с небольшими тенями, то в зависимости от направления
                if (e.LastBarsDictionary[currentSecurity.Code].Close > e.LastBarsDictionary[currentSecurity.Code].Open)
                {
                    // Если свечка растущая
                    #region Выставляем заявку на ВХОД на покупку
                    var orderBuy = new Order
                    {
                        Comment = Name + ", enter",
                        ExpiryDate = DateTime.Now.AddDays(1),
                        Portfolio = Portfolio,
                        Security = currentSecurity,
                        Type = OrderTypes.Limit,
                        Volume = SecurityVolumeDictionary[currentSecurity.Code],
                        Direction = Sides.Buy,
                        Price = currentSecurity.ShrinkPrice(e.LastBarsDictionary[currentSecurity.Code].Close)
                    };

                    this.AddInfoLog(
                        "ЗАЯВКА на ВХОД - {0}. Регистрируем заявку на {1} по цене {2} c объемом {3} - стоп на {4}",
                        currentSecurity.Code, orderBuy.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderBuy.Price.ToString(CultureInfo.InvariantCulture),
                        orderBuy.Volume.ToString(CultureInfo.InvariantCulture),
                        currentSecurity.ShrinkPrice(orderBuy.Price * (1 - StopLossPercent / 100)));

                    var orderBuy2 = new Order
                    {
                        Comment = Name + ", enter2",
                        ExpiryDate = DateTime.Now.AddDays(1),
                        Portfolio = Portfolio,
                        Security = currentSecurity,
                        Type = OrderTypes.Limit,
                        Volume = SecurityVolumeDictionary[currentSecurity.Code],
                        Direction = Sides.Buy,
                        Price = currentSecurity.ShrinkPrice(e.LastBarsDictionary[currentSecurity.Code].Close)
                    };

                    this.AddInfoLog(
                        "ЗАЯВКА на ВХОД - {0}. Регистрируем заявку №2 на {1} по цене {2} c объемом {3} - стоп на {4}",
                        currentSecurity.Code, orderBuy2.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderBuy2.Price.ToString(CultureInfo.InvariantCulture),
                        orderBuy2.Volume.ToString(CultureInfo.InvariantCulture),
                        currentSecurity.ShrinkPrice(orderBuy2.Price * (1 - StopLossPercent / 100)));

                    RegisterOrder(orderBuy);
                    RegisterOrder(orderBuy2);

                    #endregion
                }
                // Если свечка падующая
                else
                {
                    #region Выставляем заявку на ВХОД на продажу
                    var orderSell = new Order
                    {
                        Comment = Name + ", enter",
                        ExpiryDate = DateTime.Now.AddDays(1),
                        Portfolio = Portfolio,
                        Security = currentSecurity,
                        Type = OrderTypes.Limit,
                        Volume = SecurityVolumeDictionary[currentSecurity.Code],
                        Direction = Sides.Sell,
                        Price = currentSecurity.ShrinkPrice(e.LastBarsDictionary[currentSecurity.Code].Close)
                    };

                    this.AddInfoLog(
                        "ЗАЯВКА на ВХОД - {0}. Регистрируем заявку на {1} по цене {2} c объемом {3} - стоп на {4}",
                        currentSecurity.Code, orderSell.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderSell.Price.ToString(CultureInfo.InvariantCulture),
                        orderSell.Volume.ToString(CultureInfo.InvariantCulture),
                        currentSecurity.ShrinkPrice(orderSell.Price * (1 + StopLossPercent / 100)));

                    var orderSell2 = new Order
                    {
                        Comment = Name + ", enter2",
                        ExpiryDate = DateTime.Now.AddDays(1),
                        Portfolio = Portfolio,
                        Security = currentSecurity,
                        Type = OrderTypes.Limit,
                        Volume = SecurityVolumeDictionary[currentSecurity.Code],
                        Direction = Sides.Sell,
                        Price = currentSecurity.ShrinkPrice(e.LastBarsDictionary[currentSecurity.Code].Close)
                    };

                    this.AddInfoLog(
                        "ЗАЯВКА на ВХОД - {0}. Регистрируем заявку №2 на {1} по цене {2} c объемом {3} - стоп на {4}",
                        currentSecurity.Code, orderSell2.Direction == Sides.Sell ? "продажу" : "покупку",
                        orderSell2.Price.ToString(CultureInfo.InvariantCulture),
                        orderSell2.Volume.ToString(CultureInfo.InvariantCulture),
                        currentSecurity.ShrinkPrice(orderSell2.Price * (1 + StopLossPercent / 100)));

                    RegisterOrder(orderSell);
                    RegisterOrder(orderSell2);

                    #endregion
                }
            }
        }

        /// <summary>
        /// Метод установки профит ордера по активной позиции
        /// </summary>
        protected override void PlaceProfitOrder(ActiveTrade trade)
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
            };

            if (trade.OrderName.EndsWith("enter2"))
            {
                profitOrder.Price = trade.Direction == Direction.Sell
                                        ? currentSecurity.
                                              ShrinkPrice((trade.Price*(1 - (TakeProfitPercent*2)/100)))
                                        : currentSecurity.
                                              ShrinkPrice(trade.Price*(1 + (TakeProfitPercent*2)/100));
            }
            else
            {
                profitOrder.Price = trade.Direction == Direction.Sell
                                        ? currentSecurity.
                                              ShrinkPrice((trade.Price*(1 - TakeProfitPercent/100)))
                                        : currentSecurity.
                                              ShrinkPrice(trade.Price*(1 + TakeProfitPercent/100));

            }

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
    }
}
