// Show the loading overlay
function showLoading() {
    document.querySelector('.loading-overlay').style.display = 'flex';
}

// Hide the loading overlay
function hideLoading() {
    document.querySelector('.loading-overlay').style.display = 'none';
}

// Example: Show the loading spinner when the page is loading
window.addEventListener('load', function() {
    hideLoading(); // Hide the loading spinner when the page is fully loaded
});

// Example: Show loading animation before navigating to another page
document.querySelectorAll('a').forEach(function(link) {
    link.addEventListener('click', function(event) {
        showLoading();
    });
});