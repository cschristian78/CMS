window.cmsSite = (function () {
    function navigateSelected(selectId) {
        const select = document.getElementById(selectId);
        if (!select || select.selectedIndex < 0) {
            alert('Select a row first.');
            return false;
        }

        const target = select.options[select.selectedIndex].value;
        if (target) {
            window.location.href = target;
        }

        return true;
    }

    return {
        navigateSelected
    };
})();
