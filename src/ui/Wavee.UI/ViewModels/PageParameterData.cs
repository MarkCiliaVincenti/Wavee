﻿using System.Windows.Input;
using DynamicData.Binding;
using DynamicData.Operators;
using ReactiveUI;

namespace Wavee.UI.ViewModels;

public class PageParameterData : AbstractNotifyPropertyChanged
{
    private readonly ICommand _nextPageCommand;
    private readonly ICommand _previousPageCommand;
    private int _currentPage;
    private int _pageCount;
    private int _pageSize;
    private int _totalCount;

    public PageParameterData(int currentPage, int pageSize)
    {
        _currentPage = currentPage;
        _pageSize = pageSize;
        var canGoNext = this.WhenAnyValue(vm => vm.CurrentPage, vm => vm.PageCount, (page, count) => page < count);
        var canGoPrevious = this.WhenAnyValue(vm => vm.CurrentPage, vm => vm.PageCount, (page, count) => page > 1);

        _nextPageCommand = ReactiveCommand.Create(() => CurrentPage = CurrentPage + 1, canGoNext);
        _previousPageCommand = ReactiveCommand.Create(() => CurrentPage = CurrentPage - 1, canGoPrevious);
    }

    public ICommand NextPageCommand => _nextPageCommand;

    public ICommand PreviousPageCommand => _previousPageCommand;

    public int TotalCount
    {
        get => _totalCount;
        private set => SetAndRaise(ref _totalCount, value);
    }

    public int PageCount
    {
        get => _pageCount;
        private set => SetAndRaise(ref _pageCount, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set => SetAndRaise(ref _currentPage, value);
    }


    public int PageSize
    {
        get => _pageSize;
        private set => SetAndRaise(ref _pageSize, value);
    }


    public void Update(IPageResponse response)
    {
        CurrentPage = response.Page;
        PageSize = response.PageSize;
        PageCount = response.Pages;
        TotalCount = response.TotalSize;
    }
}