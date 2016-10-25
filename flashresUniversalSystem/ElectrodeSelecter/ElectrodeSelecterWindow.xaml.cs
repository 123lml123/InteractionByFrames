﻿using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using bitkyFlashresUniversal.connClient.model.bean;
using bitkyFlashresUniversal.view;

namespace bitkyFlashresUniversal.ElectrodeSelecter
{
    /// <summary>
    /// ElectrodeSelecterWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ElectrodeSelecterWindow : Window
    {
        private List<BitkyCheckBox> _checkBoxList;
        private ProcessPresenter _processPresenter;
        private readonly IViewCommStatus _window;
        private readonly List<int> _badPoleInts;

        public ElectrodeSelecterWindow(IViewCommStatus window, List<int> badPoleInts)
        {
            InitializeComponent();
            _window = window;
            _badPoleInts = badPoleInts;
            InitPoleShow();
        }

        /// <summary>
        /// 初始化电极复选框的显示
        /// </summary>
        private void InitPoleShow()
        {
            _checkBoxList = new List<BitkyCheckBox>(64);
            GridCheckBox.Children.Clear();
            var id = 0;
            for (var j = 0; j < 4; j++)
                for (var i = 0; i < 16; i++)
                {
                    var bitkyCheckBox = new BitkyCheckBox();
                    //在Grid中动态添加控件
                    GridCheckBox.Children.Add(bitkyCheckBox);
                    //设定控件在Grid中的位置
                    Grid.SetRow(bitkyCheckBox, i);
                    Grid.SetColumn(bitkyCheckBox, j);
                    //将控件添加到集合中，方便下一步的使用
                    _checkBoxList.Add(bitkyCheckBox);
                    //对控件使用自定义方法进行初始化
                    bitkyCheckBox.checkBox.Content = id;
                    bitkyCheckBox.checkBox.IsChecked = true;

                    //设置输入的不良电极所代表的复选框为不选中状态
                    _badPoleInts.ForEach(badId =>
                    {
                        if (badId == id)
                        {
                            bitkyCheckBox.checkBox.IsChecked = false;
                        }
                    });

                    id++;
                }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            _checkBoxList.ForEach(bitkyCheckBox => { bitkyCheckBox.checkBox.IsChecked = false; });
        }

        private void btnconfirm_Click(object sender, RoutedEventArgs e)
        {
            var beginStr = textBoxCheckedBoxBegin.Text.Trim();
            var endStr = textBoxCheckedBoxEnd.Text.Trim();
            if (IsInt(beginStr) && IsInt(endStr))
            {
                var begin = int.Parse(beginStr);
                var end = int.Parse(endStr);
                if ((begin < 0) || (begin >= 64) || (end < 0) || (end >= 64) || (begin >= end))
                    MessageBox.Show("输入的值有误，请重新输入！", "警告");
                else
                    _checkBoxList.ForEach(bitkyCheckBox =>
                    {
                        var num = int.Parse(bitkyCheckBox.checkBox.Content.ToString());
                        if ((num >= begin) && (num <= end))
                            bitkyCheckBox.checkBox.IsChecked = true;
                    });
            }
            else
                MessageBox.Show("输入的值必须为正整数，请重新输入！", "警告");
        }

        private void btnProcess_Click(object sender, RoutedEventArgs e)
        {
            // 由复选框集合生成已选择的已排序的电极集合
            var list = InitPoleList();

            if (list.Count < 4)
            {
                MessageBox.Show("请选择至少4个电极！", "警告");
                return;
            }

            //新建数据处理类
            _processPresenter = new ProcessPresenter(list);
            var listReturn = _processPresenter.Process();
            var conn = new SQLiteConnection("Data Source = " + PresetInfo.DatabasePath + "; Version = 3;");
            conn.Open();

            var trans = conn.BeginTransaction();
            var cmd = conn.CreateCommand();
            try
            {
                cmd.CommandText = "DELETE FROM sqlite_sequence";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM DataInfo";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM ElectrodeController";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DELETE FROM ElectrodeResult";
                cmd.ExecuteNonQuery();
                listReturn.ForEach(list2 =>
                {
                    var poleGroup = new ElectrodeGroup();
                    list2.ForEach(pole =>
                    {
                        if (pole.Mode == PoleMode.A)
                            poleGroup.TypeA = pole.IdOrigin;
                        else if (pole.Mode == PoleMode.B)
                            poleGroup.TypeB = pole.IdOrigin;
                        else if (pole.Mode == PoleMode.M)
                            poleGroup.TypeM = pole.IdOrigin;
                    });
                    if (poleGroup.IsReady())
                    {
                        cmd.CommandText = "INSERT INTO " + PresetInfo.ElectrodeControllerTable +
                                          "(typeA,typeB,typeM) VALUES (" +
                                          poleGroup.TypeA + "," + poleGroup.TypeB + "," + poleGroup.TypeM + ")";
                        cmd.ExecuteNonQuery();
                    }
                    else
                    {
                        throw new Exception();
                    }
                });
                cmd.CommandText = "SELECT COUNT(*) FROM " + PresetInfo.ElectrodeControllerTable;
                var countRow = int.Parse(cmd.ExecuteScalar().ToString());
                cmd.CommandText = "INSERT INTO DataInfo (name, num) VALUES ('sum', '" + countRow + "')";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "INSERT INTO DataInfo (name, num) VALUES ('current', '1')";
                cmd.ExecuteNonQuery();
            }
            catch (Exception)
            {
                trans.Rollback();
                throw;
            }
            trans.Commit();
            conn.Close();
            Close();
            _window.SetElectrodeSuccessful(list);
            MessageBox.Show("您已勾选" + list.Count + "个电极, 共需采集数据" + listReturn.Count + "次, 请开始数据采集", "电极参数配置成功");
        }

        /// <summary>
        ///     由复选框集合生成已选择的已排序的电极集合
        /// </summary>
        /// <returns>已排序的电极集合</returns>
        private List<Electrode> InitPoleList()
        {
            //将选中的电极添加入集合
            var list = new List<Electrode>();
            _checkBoxList.ForEach(bitkyCheckBox =>
            {
                if (bitkyCheckBox.checkBox.IsChecked == true)
                    list.Add(new Electrode(int.Parse(bitkyCheckBox.checkBox.Content.ToString())));
            });
            //对集合进行排序
            Comparison<Electrode> sort = CompareSort;
            list.Sort(sort);
            var i = 1;
            //对电极重新编号
            list.ForEach(pole =>
            {
                pole.IdCurrent = i;
                i++;
            });
            return list;
        }

        /// <summary>
        ///     电极排序方法
        /// </summary>
        private static int CompareSort(Electrode x, Electrode y)
        {
            if (x.IdOrigin > y.IdOrigin)
                return 1;
            if (x.IdOrigin < y.IdOrigin)
                return -1;
            return 0;
        }

        /// <summary>
        ///     判断是正整数
        /// </summary>
        /// <param name="value">待匹配的文本</param>
        /// <returns>匹配结果</returns>
        private static bool IsInt(string value) //判断是正整数
        {
            value = value.Trim();
            return Regex.IsMatch(value, @"^[1-9]\d*|0$");
        }
    }
}