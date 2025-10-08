// NimbusLedger Website JavaScript (adapted from NimbusOne)

document.addEventListener('DOMContentLoaded', function() {
    initializeAnimations();
    initializeNavbar();
});

function initializeAnimations() {
    if (typeof AOS !== 'undefined') {
        AOS.init({ duration: 800, easing: 'ease-in-out', once: true, mirror: false, offset: 100 });
    }
}

function initializeNavbar() {
    const navbar = document.querySelector('.navbar');
    if (!navbar) return;
    window.addEventListener('scroll', function() {
        if (window.scrollY > 50) navbar.classList.add('scrolled'); else navbar.classList.remove('scrolled');
    });
}
