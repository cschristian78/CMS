window.cmsAdmin = (function () {
    function byId(id) {
        return document.getElementById(id);
    }

    function selectedOption(listId) {
        const list = byId(listId);
        if (!list || list.selectedIndex < 0) {
            return null;
        }
        return list.options[list.selectedIndex];
    }

    function setValue(id, value) {
        const element = byId(id);
        if (element) {
            element.value = value || '';
        }
    }

    function openTab(tabName) {
        document.querySelectorAll('.admin-tab-panel').forEach(panel => panel.classList.remove('active'));
        document.querySelectorAll('.admin-tabs button').forEach(button => button.classList.remove('active'));

        const panel = byId('admin-tab-' + tabName);
        if (panel) {
            panel.classList.add('active');
        }

        const button = document.querySelector('.admin-tabs button[data-tab="' + tabName + '"]');
        if (button) {
            button.classList.add('active');
        }
    }

    function submitSelectedId(listId, targetInputId, message) {
        const option = selectedOption(listId);
        if (!option) {
            alert(message || 'Select a row first.');
            return false;
        }
        setValue(targetInputId, option.value);
        return true;
    }

    function clearUserForm() {
        setValue('user_UserID', '0');
        setValue('user_FirstName', '');
        setValue('user_LastName', '');
        setValue('user_Title', '');
        setValue('user_Department', '');
        setValue('user_Email', '');
        setValue('user_Phone', '');
        setValue('user_Company', 'BMS');
        setValue('user_Address', '');
        setValue('user_City', '');
        setValue('user_State', '');
        setValue('user_Country', '');
    }

    function editUserFromSelection() {
        const option = selectedOption('usersList');
        if (!option) {
            alert('Select a user first.');
            return;
        }

        setValue('user_UserID', option.value);
        setValue('user_FirstName', option.dataset.firstName);
        setValue('user_LastName', option.dataset.lastName);
        setValue('user_Title', option.dataset.title);
        setValue('user_Department', option.dataset.department);
        setValue('user_Email', option.dataset.email);
        setValue('user_Phone', option.dataset.phone);
        setValue('user_Company', option.dataset.company);
        setValue('user_Address', option.dataset.address);
        setValue('user_City', option.dataset.city);
        setValue('user_State', option.dataset.state);
        setValue('user_Country', option.dataset.country);
        setValue('user_StatusID', option.dataset.statusId);
    }

    function clearRoleForm() {
        setValue('role_UserRoleID', '0');
        setValue('role_UserRoleName', '');
    }

    function editRoleFromSelection() {
        const option = selectedOption('rolesList');
        if (!option) {
            alert('Select a user role first.');
            return;
        }
        setValue('role_UserRoleID', option.value);
        setValue('role_UserRoleName', option.dataset.roleName);
        setValue('role_StatusID', option.dataset.statusId);
    }

    function clearGroupForm() {
        setValue('group_UserGroupID', '0');
        setValue('group_UserGroupNumber', '0');
        setValue('group_UserGroupName', '');
    }

    function editGroupMemberFromSelection() {
        const option = selectedOption('groupMembersList');
        if (!option) {
            alert('Select a user group row first.');
            return;
        }
        setValue('group_UserGroupID', option.value);
        setValue('group_UserGroupNumber', option.dataset.groupNumber);
        setValue('group_UserGroupName', option.dataset.groupName);
        setValue('group_UserID', option.dataset.userId);
        setValue('group_UserRoleID', option.dataset.roleId);
    }

    function populateGroupFormFromSideLists() {
        const userOption = selectedOption('bmsUsersList');
        const roleOption = selectedOption('groupRolesList');
        if (!userOption || !roleOption) {
            alert('Select both a user and a user role.');
            return;
        }
        setValue('group_UserID', userOption.value);
        setValue('group_UserRoleID', roleOption.value);
    }

    function clearProjectTypeForm() {
        setValue('projectType_ProjectTypeID', '0');
        setValue('projectType_ProjectTypeCode', '');
        setValue('projectType_ProjectTypeDescription', '');
    }

    function editProjectTypeFromSelection() {
        const option = selectedOption('projectTypesList');
        if (!option) {
            alert('Select a project type first.');
            return;
        }
        setValue('projectType_ProjectTypeID', option.value);
        setValue('projectType_ProjectTypeCode', option.dataset.code);
        setValue('projectType_ProjectTypeDescription', option.dataset.desc);
    }

    function clearBusinessUnitForm() {
        setValue('businessUnit_BUID', '0');
        setValue('businessUnit_BUName', '');
        setValue('businessUnit_BUDescription', '');
    }

    function editBusinessUnitFromSelection() {
        const option = selectedOption('businessUnitsList');
        if (!option) {
            alert('Select a business unit first.');
            return;
        }
        setValue('businessUnit_BUID', option.value);
        setValue('businessUnit_BUName', option.dataset.name);
        setValue('businessUnit_BUDescription', option.dataset.desc);
    }

    function clearStatusForm() {
        setValue('status_StatusID', '0');
        setValue('status_StatusCode', '');
        setValue('status_StatusDescription', '');
    }

    function editStatusFromSelection() {
        const option = selectedOption('statusesList');
        if (!option) {
            alert('Select a status first.');
            return;
        }
        setValue('status_StatusID', option.value);
        setValue('status_StatusCode', option.dataset.code);
        setValue('status_StatusDescription', option.dataset.desc);
    }

    document.addEventListener('DOMContentLoaded', function () {
        const params = new URLSearchParams(window.location.search);
        openTab(params.get('tab') || 'server');
    });

    return {
        openTab,
        submitSelectedId,
        clearUserForm,
        editUserFromSelection,
        clearRoleForm,
        editRoleFromSelection,
        clearGroupForm,
        editGroupMemberFromSelection,
        populateGroupFormFromSideLists,
        clearProjectTypeForm,
        editProjectTypeFromSelection,
        clearBusinessUnitForm,
        editBusinessUnitFromSelection,
        clearStatusForm,
        editStatusFromSelection
    };
})();
